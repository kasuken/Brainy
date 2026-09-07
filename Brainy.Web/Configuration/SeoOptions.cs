using System.ComponentModel.DataAnnotations;

namespace Brainy.Web.Configuration;

/// <summary>Configuration for public-site SEO metadata and discovery endpoints.</summary>
public sealed class SeoOptions
{
    /// <summary>Absolute public origin used for canonical and sitemap URLs.</summary>
    [Required, Url]
    public string SiteOrigin { get; set; } = string.Empty;

    /// <summary>Public product name used by structured metadata.</summary>
    [Required]
    public string SiteName { get; set; } = "Brainy";

    /// <summary>Default description used by structured metadata.</summary>
    [Required]
    public string SiteDescription { get; set; } = string.Empty;

    /// <summary>Path to the social preview image, relative to the public origin.</summary>
    [Required]
    public string SocialPreviewImagePath { get; set; } = "/img/og-preview.png";
}
