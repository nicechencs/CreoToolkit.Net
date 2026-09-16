using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtSelect;

public static class PtSelectRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtSelectCopies(app, modelName, modelType);
        RegisterPtSelectHighlightFirst(app, modelName, modelType);
        RegisterPtSelectUnhighlightFirst(app, modelName, modelType);
        RegisterPtSelectDisplayFirst(app, modelName, modelType);
        }

    private static void RegisterPtSelectCopies(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_select: owned selection copy/free 生命周期切片。
        app.Command("pt.select.copies", "PT_SELECT_COPIES", "PT_SELECT_COPIES_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = session.Models.Retrieve(modelName, modelType);
            using var selection = session.Selections.CreateModelSelection(model);
            var first = selection.Count > 0 ? $"; first={selection[0].Label}" : "";
            ctx.Messages.Info($"{model.FullName} selection copies={selection.Count}{first}");
        });
    }

    private static void RegisterPtSelectHighlightFirst(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_select/TestSelect.c: ProSelectionHighlight 的 owned copy 作用域切片。
        app.Command("pt.select.highlight-first", "PT_SELECT_HIGHLIGHT_FIRST", "PT_SELECT_HIGHLIGHT_FIRST_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = session.Models.Retrieve(modelName, modelType);
            using var selection = session.Selections.CreateModelSelection(model);
            if (selection.Count == 0)
            {
                ctx.Messages.Info($"No selection copy found on {model.FullName}");
                return;
            }

            selection[0].Highlight();
            ctx.Messages.Info($"Highlighted selection copy: {selection[0].Label} on {model.FullName}");
        });
    }

    private static void RegisterPtSelectUnhighlightFirst(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_select/TestSelect.c: ProSelectionUnhighlight 的 owned copy 作用域切片。
        app.Command("pt.select.unhighlight-first", "PT_SELECT_UNHIGHLIGHT_FIRST",
            "PT_SELECT_UNHIGHLIGHT_FIRST_HELP", ctx =>
            {
                if (!ctx.RequireSession(out var session)) return;

                var model = session.Models.Retrieve(modelName, modelType);
                using var selection = session.Selections.CreateModelSelection(model);
                if (selection.Count == 0)
                {
                    ctx.Messages.Info($"No selection copy found on {model.FullName}");
                    return;
                }

                selection[0].Highlight();
                selection[0].UnHighlight();
                ctx.Messages.Info($"Unhighlighted selection copy: {selection[0].Label} on {model.FullName}");
            });
    }

    private static void RegisterPtSelectDisplayFirst(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_select/TestSelect.c: ProSelectionDisplay 的 owned copy 作用域切片。
        app.Command("pt.select.display-first", "PT_SELECT_DISPLAY_FIRST", "PT_SELECT_DISPLAY_FIRST_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = session.Models.Retrieve(modelName, modelType);
            using var selection = session.Selections.CreateModelSelection(model);
            if (selection.Count == 0)
            {
                ctx.Messages.Info($"No selection copy found on {model.FullName}");
                return;
            }

            selection[0].Display();
            ctx.Messages.Info($"Displayed selection copy: {selection[0].Label} on {model.FullName}");
        });

    }
}
