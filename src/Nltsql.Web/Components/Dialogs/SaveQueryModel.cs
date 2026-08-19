namespace Nltsql.Web.Components.Dialogs;

/// <summary>Editable fields of the save dialog.</summary>
public sealed class SaveQueryModel
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
}
