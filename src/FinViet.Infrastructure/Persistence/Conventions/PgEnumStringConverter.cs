using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.NameTranslation;

namespace FinViet.Infrastructure.Persistence.Conventions;

/// <summary>
/// Builds EF value converters that bridge a CLR <see cref="string"/> property to a Postgres
/// enum column mapped via <c>NpgsqlDataSourceBuilder.MapEnum&lt;TEnum&gt;()</c>.
/// <para>
/// Entities keep their snake_case string values (e.g. <c>"transfer_out"</c>); persistence binds
/// them as the mapped CLR enum so Npgsql sends the enum OID instead of <c>text</c>. Sending text
/// fails on write — Postgres refuses to cast it: <c>"column is of type X but expression is of
/// type text"</c>.
/// </para>
/// <para>
/// Input is matched case-insensitively so legacy upper-case writers (e.g. "INCOME"/"EXPENSE"
/// from the statement importers) still bind correctly. Reads always return the canonical
/// lower-case label, preserving prior behaviour.
/// </para>
/// </summary>
public static class PgEnumStringConverter
{
    // Mirrors the default name translator used by MapEnum (snake_case), so the labels produced
    // here match the Postgres enum labels exactly.
    private static readonly NpgsqlSnakeCaseNameTranslator Translator = new();

    public static ValueConverter<string, TEnum> Create<TEnum>() where TEnum : struct, Enum
    {
        var toLabel = Enum.GetValues<TEnum>()
            .ToDictionary(member => member, member => Translator.TranslateMemberName(member.ToString()));

        var fromLabel = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var (member, label) in toLabel)
            fromLabel[label] = member;

        return new ValueConverter<string, TEnum>(
            model => fromLabel[model],
            provider => toLabel[provider]);
    }

    /// <summary>Creates a converter for an optional string-backed enum column.</summary>
    public static ValueConverter<string?, TEnum> CreateNullable<TEnum>() where TEnum : struct, Enum
    {
        var toLabel = Enum.GetValues<TEnum>()
            .ToDictionary(member => member, member => Translator.TranslateMemberName(member.ToString()));

        var fromLabel = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var (member, label) in toLabel)
            fromLabel[label] = member;

        // EF never sends null values through a value converter; nullable values pass directly to
        // the provider. The null-forgiving operator documents that contract to the compiler.
        return new ValueConverter<string?, TEnum>(
            model => fromLabel[model!],
            provider => toLabel[provider]);
    }
}
