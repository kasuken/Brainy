namespace Brainy.Web;

/// <summary>
/// Marker type with no members, used only as <c>IStringLocalizer&lt;SharedResource&gt;</c>'s
/// generic parameter so components can share one small resource file (SharedResource.resx /
/// SharedResource.it-IT.resx) for strings repeated across pages — common dialog actions
/// (Save, Cancel, Close) and the like — instead of duplicating them per component.
/// Deliberately namespaced <c>Brainy.Web</c> (the project's root namespace), not
/// <c>Brainy.Web.Resources</c>: <see cref="ResourceManagerStringLocalizerFactory"/> derives a
/// resource's base name from <c>{RootNamespace}.{ResourcesPath}.{namespace relative to
/// RootNamespace}.{TypeName}</c>, so a type whose own namespace also contained "Resources"
/// (matching <c>LocalizationOptions.ResourcesPath</c>) would double that segment and the
/// resource would never resolve. This file lives under Resources/ on disk purely so it sits
/// next to the .resx files it names; its namespace does not need to match the folder.
/// Also deliberately named <c>SharedResourceMarker.cs</c> rather than <c>SharedResource.cs</c>:
/// MSBuild auto-pairs a .resx with a same-base-name .cs file in the same folder (the WinForms
/// "Form1.cs"/"Form1.resx" convention) and then embeds the resource under THAT file's
/// namespace instead of the folder-derived one — which would silently break resolution again
/// even with the namespace fix above, since a same-named .cs/.resx pair here would end up
/// embedded as "Brainy.Web.SharedResource" with no "Resources." segment at all.
/// </summary>
public sealed class SharedResource;
