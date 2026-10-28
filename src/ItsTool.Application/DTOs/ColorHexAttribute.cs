using System.ComponentModel.DataAnnotations;

namespace ItsTool.Application.DTOs;

/// <summary>
/// A colour as #RRGGBB, or nothing. The pages put these values into styles and markup,
/// so anything else (CSS, HTML) is refused.
/// </summary>
public sealed class ColorHexAttribute : RegularExpressionAttribute
{
    public ColorHexAttribute() : base("^#[0-9A-Fa-f]{6}$")
    {
        ErrorMessage = "{0} must be a colour in the form #RRGGBB.";
    }
}
