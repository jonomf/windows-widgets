using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace JonoWidgets;

/// <summary>
/// What the Widgets board calls. Every call copies what it needs out of the arguments (they're only
/// valid during the call) and hands it to <see cref="Hub"/>. Nothing here may throw back to the host.
/// </summary>
[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("B7F976F3-893C-4FB3-A012-23837D00D64B")]
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private static string SizeOf(WidgetContext c) => c.Size.ToString().ToLowerInvariant();

    public void CreateWidget(WidgetContext widgetContext)
    {
        string id = widgetContext.Id, def = widgetContext.DefinitionId, size = SizeOf(widgetContext);
        Guard("create", () => Hub.Create(id, def, size));
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Guard("delete", () => Hub.Delete(widgetId));
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var c = actionInvokedArgs.WidgetContext;
        string id = c.Id, def = c.DefinitionId, size = SizeOf(c);
        string verb = actionInvokedArgs.Verb ?? "", data = actionInvokedArgs.Data ?? "";
        Guard("action " + verb, () => Hub.Action(id, def, size, verb, data));
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var c = contextChangedArgs.WidgetContext;
        string id = c.Id, def = c.DefinitionId, size = SizeOf(c);
        Guard("context", () => Hub.Resized(id, def, size));
    }

    public void Activate(WidgetContext widgetContext)
    {
        string id = widgetContext.Id, def = widgetContext.DefinitionId, size = SizeOf(widgetContext);
        Guard("activate", () => Hub.Activate(id, def, size));
    }

    public void Deactivate(string widgetId)
    {
        Guard("deactivate", () => Hub.Deactivate(widgetId));
    }

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs)
    {
        var c = customizationRequestedArgs.WidgetContext;
        string id = c.Id, def = c.DefinitionId, size = SizeOf(c);
        Guard("customize", () => Hub.Customize(id, def, size));
    }

    private static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error(what, e); }
    }
}
