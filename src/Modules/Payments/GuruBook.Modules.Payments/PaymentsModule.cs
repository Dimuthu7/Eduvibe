using GuruBook.Modules.Payments.Endpoints;
using GuruBook.Modules.Payments.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Payments;

/// <summary>
/// Paying a fee by bank transfer or LankaQR (a Pro feature). A teacher puts up their account details and QR once; their students and the
/// parents they linked see what to pay, what to write on the transfer, and send back a slip or a reference. The teacher confirms it, which
/// records an ordinary bank transfer payment in fees, or rejects it with a reason. GuruBook never touches the money: it only carries the
/// claim and the teacher's word on it.
/// </summary>
public sealed class PaymentsModule : IModule
{
    public const string ModuleName = "payments";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration, ModuleName);
        services.AddScoped<ClaimService>();
        services.AddScoped<SlipCleaner>();
        services.AddScoped<IPersonalDataSource, PaymentsPersonalData>();
        services.AddHostedService<SlipCleanupJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Payments");
        TeacherEndpoints.Map(group);
        PayerEndpoints.Map(group);
    }
}
