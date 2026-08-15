using System.Text;
using System.Text.RegularExpressions;
using FinViet.Application.Common.Exceptions;

namespace FinViet.Infrastructure.Features.Categories.Commands.UploadCategoryIcon;

internal static class CategoryIconValidationRules
{
    private const int MaximumFileSize = 200 * 1024;
    private const string AllowedContentType = "image/svg+xml";

    private static readonly Regex ScriptOrEventHandlerPattern = new(
        @"<script|on[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Validate(byte[] content, string contentType)
    {
        if (!string.Equals(contentType, AllowedContentType, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Only SVG icons are allowed.");

        if (content.Length == 0 || content.Length > MaximumFileSize)
            throw new BadRequestException("Icon file size must be between 1 byte and 200 KB.");

        var text = Encoding.UTF8.GetString(content).TrimStart('﻿').TrimStart();
        if (!text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("File content is not a valid SVG.");

        if (ScriptOrEventHandlerPattern.IsMatch(text))
            throw new BadRequestException("SVG icon must not contain scripts or event handlers.");
    }
}
