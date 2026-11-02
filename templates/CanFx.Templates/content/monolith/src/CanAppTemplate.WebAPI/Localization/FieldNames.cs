using System.Globalization;
using CanFx.Localization;
using FluentValidation;

namespace CanAppTemplate.WebAPI.Localization;

/// <summary>
/// Doğrulama mesajlarındaki alan adlarını isteğin diline çevirir ("'First Name' boş olamaz." yerine "'Ad' boş olamaz.").
/// Arama sırası: <c>{özellik}.fields.{Özellik}</c> (ör. brands.fields.Name) → <c>fields.{Özellik}</c>; çeviri yoksa
/// FluentValidation'ın varsayılanı. Metinler: Application/Localization/fields.*.json ve özellik dosyaları.
/// </summary>
public static class FieldNames
{
    public static void UseLocalizedFieldNames(this IServiceProvider services)
    {
        JsonLocalizationStore store = services.GetRequiredService<JsonLocalizationStore>();

        ValidatorOptions.Global.DisplayNameResolver = (type, member, _) =>
        {
            CultureInfo culture = CultureInfo.CurrentUICulture;
            string? name = member is null ? null
                : (Feature(type) is { } feature ? store.Find($"{feature}.fields.{member.Name}", culture) : null)
                    ?? store.Find($"fields.{member.Name}", culture);
            return name!; // null: FluentValidation'ın varsayılan adı ("First Name")
        };
    }

    /// <summary><c>...Application.Features.UserAuthorizations.Commands...</c> → <c>user_authorizations</c>.</summary>
    private static string? Feature(Type type)
    {
        string? ns = type.Namespace;
        int start = ns?.IndexOf(".Features.", StringComparison.Ordinal) ?? -1;
        if (ns is null || start < 0)
            return null;

        string name = ns[(start + ".Features.".Length)..].Split('.')[0];
        return string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
