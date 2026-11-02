using System.Text.RegularExpressions;

namespace CanFx.Generator.Engine;

/// <summary>
/// Yeni entity dosyası (Domain'de). Taban sınıf seçenekleri ve ekledikleri alanlar dosyada yorum olarak durur;
/// geliştirici sınıf satırındaki tabanı değiştirerek seçer (ek ekran yok). Ardından: <c>canfxgen generate crud {Ad}</c>.
/// </summary>
public static partial class EntityGenerator
{
    private const string Header = "// canfxgen ile üretildi; artık senin kodun, istediğin gibi değiştir.";

    /// <param name="layout">Çözüm.</param>
    /// <param name="name">Entity adı (tekil, PascalCase): <c>Brand</c>.</param>
    /// <param name="directory">
    /// Dosyanın yazılacağı klasör (Domain projesinin içinde). Boşsa <c>Domain/Entities</c>. Namespace klasörden çıkar:
    /// <c>Domain/Catalog</c> → <c>{Kök}.Domain.Catalog</c>.
    /// </param>
    /// <param name="idType">Id tipi: <c>Guid</c> (kodda üretilir), <c>int</c>, <c>long</c> (veritabanı üretir).</param>
    public static GenerationPlan Plan(SolutionLayout layout, string name, string? directory = null, string idType = "Guid")
    {
        ArgumentNullException.ThrowIfNull(layout);
        name = Names.Pascal(name?.Trim() ?? string.Empty);
        if (!Identifier().IsMatch(name))
            throw new GeneratorException($"Geçersiz entity adı: '{name}'. Harfle başlamalı; harf ve rakam içerebilir (ör. Brand).");
        if (idType is not ("Guid" or "int" or "long"))
            throw new GeneratorException($"Geçersiz Id tipi: '{idType}'. Guid, int ya da long olmalı.");

        string folder = string.IsNullOrWhiteSpace(directory) ? Path.Combine(layout.Domain, "Entities") : Path.GetFullPath(directory);
        string relative = Path.GetRelativePath(layout.Domain, folder);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new GeneratorException($"Entity Domain projesinin içinde olmalı: {folder}");

        string ns = relative == "." ? $"{layout.RootNamespace}.Domain"
            : $"{layout.RootNamespace}.Domain.{relative.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.')}";

        var plan = new GenerationPlan();
        plan.AddFile(Path.Combine(folder, name + ".cs"), Entity(name, ns, idType));
        return plan;
    }

    internal static string Entity(string name, string ns, string id) => $$"""
        {{Header}}
        using CanFx.Domain.Auditing;
        using CanFx.Domain.Entities;

        namespace {{ns}};

        // Taban sınıf: aşağıdakilerden birini seç (sınıf satırında değiştir). Id tipi <...> içinde: Guid (kodda üretilir,
        // sıralı v7) ya da int / long (veritabanı üretir). Değiştirdikten sonra: canfxgen generate crud {{name}}
        //
        //   Entity<{{id}}>                    Id
        //   AuditedEntity<{{id}}>             Id + CreatedAt, CreatedBy, UpdatedAt, UpdatedBy (kayıtta otomatik dolar)
        //   FullAuditedEntity<{{id}}>         AuditedEntity + IsDeleted, DeletedAt, DeletedBy: silme kaydı silmez,
        //                                     işaretler; silinen kayıt sorgulardan kendiliğinden düşer (soft delete)
        //   AggregateRoot<{{id}}>             Entity + domain event: RaiseDomainEvent(...) kayıtla birlikte yayınlanır
        //   AuditedAggregateRoot<{{id}}>      AuditedEntity + domain event
        //   FullAuditedAggregateRoot<{{id}}>  FullAuditedEntity + domain event
        //
        // Ek seçenekler:
        //   , IMultiTenant<Guid>         TenantId: kayıtlar tenant'a göre kendiliğinden süzülür (çoklu kiracılık açıksa)
        //   [Audited] (sınıfın üstüne)   Her değişiklik eski/yeni değerleriyle AuditLogs tablosuna yazılır

        /// <summary>TODO(canfxgen): {{Names.Words(name)}} neyi temsil ediyor?</summary>
        public class {{name}} : FullAuditedEntity<{{id}}>
        {
            // TODO(canfxgen): Alanları ekle. Metinlerde uzunluk sabiti tanımla; doğrulama ve sütun uzunluğu ondan üretilir:
            // public const int NameMaxLength = 100;
            // public string Name { get; set; } = string.Empty;
        }

        """;

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex Identifier();
}
