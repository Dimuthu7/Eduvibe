using System.Linq.Expressions;
using System.Reflection;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduVibe.Shared.Privacy;

/// <summary>
/// Reads and deletes a module's rows by tenant or by enrolment straight from its EF model, so a module's personal data source needs
/// no per-table code and a new table is covered the day it is added. Secrets (fields marked <see cref="NotAuditedAttribute"/>:
/// password hashes, invite tokens) are never exported. Query filters are ignored on purpose: the caller is the Super Admin.
/// </summary>
public static class ModuleData
{
    /// <summary>Every tenant-owned table's rows for one teacher.</summary>
    public static Task<PersonalData> ReadTenantAsync(DbContext db, Guid tenantId, CancellationToken ct) =>
        ReadAsync(db, TenantOwnedTypes(db), type => Equal(type, nameof(ITenantOwned.TenantId), tenantId), ct);

    /// <summary>Every table with an EnrolmentId column: the rows that hang off these enrolments.</summary>
    public static Task<PersonalData> ReadByEnrolmentsAsync(DbContext db, IReadOnlyCollection<Guid> enrolmentIds, CancellationToken ct) =>
        enrolmentIds.Count == 0
            ? Task.FromResult(new PersonalData())
            : ReadAsync(db, db.Model.GetEntityTypes().Where(t => t.FindProperty("EnrolmentId")?.ClrType == typeof(Guid)), type => Contains(type, "EnrolmentId", enrolmentIds), ct);

    /// <summary>Deletes every tenant-owned row of one teacher, children before parents. Bypasses change tracking, so no audit row is written per row.</summary>
    public static async Task EraseTenantAsync(DbContext db, Guid tenantId, CancellationToken ct)
    {
        foreach (var type in InDeleteOrder(TenantOwnedTypes(db).ToList()))
        {
            await DeleteAsync(db, type, Equal(type.ClrType, nameof(ITenantOwned.TenantId), tenantId), ct);
        }
    }

    /// <summary>One row as column name to value, without secrets.</summary>
    public static Dictionary<string, object?> Project(IEntityType type, object entity) =>
        type.GetProperties()
            .Where(p => p.PropertyInfo?.IsDefined(typeof(NotAuditedAttribute), inherit: true) != true)
            .ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(entity));

    private static IEnumerable<IEntityType> TenantOwnedTypes(DbContext db) =>
        db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && t.BaseType is null);

    // Dependents before the tables they point at: repeatedly take a table nothing left refers to.
    private static IEnumerable<IEntityType> InDeleteOrder(List<IEntityType> types)
    {
        var left = types.ToList();
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(t => !left.Any(o => o != t && o.GetForeignKeys().Any(fk => fk.PrincipalEntityType == t))) ?? left[0];
            left.Remove(next);
            yield return next;
        }
    }

    private static async Task<PersonalData> ReadAsync(
        DbContext db, IEnumerable<IEntityType> types, Func<Type, LambdaExpression> where, CancellationToken ct)
    {
        var data = new PersonalData();
        foreach (var type in types)
        {
            var rows = await (Task<IReadOnlyList<object>>)typeof(ModuleData)
                .GetMethod(nameof(ReadTable), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type.ClrType)
                .Invoke(null, [db, type, where(type.ClrType), ct])!;
            if (rows.Count > 0)
            {
                data[type.GetTableName() ?? type.ClrType.Name] = rows;
            }
        }

        return data;
    }

    private static async Task<IReadOnlyList<object>> ReadTable<T>(DbContext db, IEntityType type, Expression<Func<T, bool>> where, CancellationToken ct)
        where T : class
    {
        var rows = await db.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(where).ToListAsync(ct);
        return [.. rows.Select(r => (object)Project(type, r))];
    }

    private static Task DeleteAsync(DbContext db, IEntityType type, LambdaExpression where, CancellationToken ct) =>
        (Task)typeof(ModuleData)
            .GetMethod(nameof(DeleteTable), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.ClrType)
            .Invoke(null, [db, where, ct])!;

    private static Task<int> DeleteTable<T>(DbContext db, Expression<Func<T, bool>> where, CancellationToken ct) where T : class =>
        db.Set<T>().IgnoreQueryFilters().Where(where).ExecuteDeleteAsync(ct);

    // e => e.Property == value
    private static LambdaExpression Equal(Type entity, string property, Guid value)
    {
        var e = Expression.Parameter(entity, "e");
        return Expression.Lambda(Expression.Equal(Expression.Property(e, property), Expression.Constant(value)), e);
    }

    // e => ids.Contains(e.Property)
    private static LambdaExpression Contains(Type entity, string property, IReadOnlyCollection<Guid> values)
    {
        var e = Expression.Parameter(entity, "e");
        var list = values.ToList();
        return Expression.Lambda(Expression.Call(Expression.Constant(list), nameof(List<Guid>.Contains), null, Expression.Property(e, property)), e);
    }

}
