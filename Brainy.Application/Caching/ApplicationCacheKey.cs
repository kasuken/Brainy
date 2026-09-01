using System.Globalization;
using System.Text;

namespace Brainy.Application.Caching;

internal static class ApplicationCacheKey
{
    public static string TimeZoneTag { get; } = Create("tag", "time-zone");

    public static string Create(params object?[] segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment is null)
            {
                builder.Append("-1:");
                continue;
            }

            var value = segment switch
            {
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => segment.ToString() ?? string.Empty
            };

            builder.Append(value.Length)
                .Append(':')
                .Append(value);
        }

        return builder.ToString();
    }

    public static string EntityTypeTag<TEntity>() =>
        Create("tag", "entity-type", typeof(TEntity).FullName ?? typeof(TEntity).Name);

    public static string EntityTag<TEntity>(Guid id) =>
        Create("tag", "entity", typeof(TEntity).FullName ?? typeof(TEntity).Name, id);
}
