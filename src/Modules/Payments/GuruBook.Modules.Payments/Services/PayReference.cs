using System.Security.Cryptography;
using System.Text;

namespace GuruBook.Modules.Payments.Services;

/// <summary>
/// The short code a payer writes on the transfer. It is worked out from the fee itself, so the pay screen can show it before any money moves
/// and the teacher's list shows the same code afterwards: a line in a bank statement can then be matched to one student and one month.
/// Letters that are easily confused by hand (I, L, O, U) are left out.
/// </summary>
public static class PayReference
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 6;

    public static string For(Guid enrolmentId, string month)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{enrolmentId:N}:{month}"));
        var code = new StringBuilder("GB", 2 + Length);
        for (var i = 0; i < Length; i++)
        {
            code.Append(Alphabet[bytes[i] % Alphabet.Length]);
        }

        return code.ToString();
    }
}
