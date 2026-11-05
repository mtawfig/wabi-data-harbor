using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Applications;

/// <summary>
/// Purpose-specific Data Protection for connection strings (specification §11). The key ring lives outside the
/// configuration database (file system, rotated automatically, old keys retained for decryption). The fingerprint
/// is an HMAC over the normalized string keyed with a random per-installation salt stored in PlatformSettings,
/// so equal sources collide across organizations while the hash reveals nothing about the string.
/// </summary>
public sealed class ConnectionStringProtector : IConnectionStringProtector
{
    public const string Purpose = "WabiInsight.Applications.ConnectionString.v1";
    public const string FingerprintSaltKey = "Applications.ConnectionFingerprintSalt";

    private readonly IDataProtector _protector;
    private readonly ConfigurationDbContext _context;
    private readonly IClock _clock;

    public ConnectionStringProtector(IDataProtectionProvider provider, ConfigurationDbContext context, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
        _context = context;
        _clock = clock;
    }

    public string Protect(string connectionString) => _protector.Protect(connectionString);

    public string Unprotect(string payload) => _protector.Unprotect(payload);

    public async Task<string> FingerprintAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var salt = await _context.PlatformSettings.AsNoTracking().Where(s => s.Key == FingerprintSaltKey).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        if (salt is null)
        {
            salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _context.PlatformSettings.Add(new PlatformSetting { Key = FingerprintSaltKey, Value = salt, Description = "Random salt for application connection fingerprints (never reveals connection strings).", IsSensitive = true, UpdatedAt = _clock.UtcNow });
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another request created it first.
                _context.ChangeTracker.Clear();
                salt = await _context.PlatformSettings.AsNoTracking().Where(s => s.Key == FingerprintSaltKey).Select(s => s.Value).SingleAsync(cancellationToken);
            }
        }

        var hash = HMACSHA256.HashData(Convert.FromHexString(salt), Encoding.UTF8.GetBytes(connectionString));
        return Convert.ToHexStringLower(hash);
    }
}
