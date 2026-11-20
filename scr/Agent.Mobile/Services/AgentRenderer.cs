using Agent.Core.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Agent.Mobile.Services;

/// <summary>
/// Renders only the small allowlisted Agent UI vocabulary. Model output never becomes
/// XAML, a binding expression, a command line, an external URL, or arbitrary navigation.
/// </summary>
public sealed class AgentUiRenderer
{
    public View Render(AgentUiDocument document, Func<string, Task>? onAction = null)
    {
        var validation = AgentUiDocumentValidator.Validate(document);
        if (!validation.IsValid)
            return BuildError(string.Join(" ", validation.Errors));

        var content = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(0, 4) };
        if (!string.IsNullOrWhiteSpace(document.Title))
            content.Children.Add(new Label
            {
                Text = document.Title,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = ResourceColor("TextPrimary", Colors.Black)
            });

        foreach (var block in document.Blocks!)
            content.Children.Add(RenderBlock(block, onAction));
        return content;
    }

    private View RenderBlock(AgentUiBlock block, Func<string, Task>? onAction) => block.Type switch
    {
        "text" => new Label { Text = block.Text ?? string.Empty, FontSize = 13, TextColor = ResourceColor("TextPrimary", Colors.Black) },
        "card" => BuildCard(block),
        "code" => BuildCode(block),
        "status" => BuildStatus(block),
        "keyValue" => BuildKeyValue(block),
        "list" => BuildList(block, false),
        "checklist" => BuildList(block, true),
        "progress" => BuildProgress(block),
        "action" => BuildAction(block, onAction),
        _ => BuildError("Componente de interface desconhecido.")
    };

    private View BuildCard(AgentUiBlock block)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        AddTitle(stack, block.Title);
        if (!string.IsNullOrWhiteSpace(block.Text))
            stack.Children.Add(new Label { Text = block.Text, FontSize = 12, TextColor = ResourceColor("TextPrimary", Colors.Black) });
        return Wrap(stack);
    }

    private View BuildCode(AgentUiBlock block)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        AddTitle(stack, block.Title);
        stack.Children.Add(new Label
        {
            Text = block.Text ?? string.Empty,
            FontFamily = "monospace",
            FontSize = 11,
            TextColor = ResourceColor("TextPrimary", Colors.Black)
        });
        return new Border
        {
            Padding = 12,
            BackgroundColor = ResourceColor("CodeSurface", Colors.LightGray),
            Stroke = ResourceColor("Border", Colors.Gray),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = stack
        };
    }

    private View BuildStatus(AgentUiBlock block)
    {
        var color = (block.Status ?? "info").ToLowerInvariant() switch
        {
            "success" or "completed" => ResourceColor("Success", Colors.Green),
            "warning" or "pending" => ResourceColor("Warning", Colors.Orange),
            "error" or "failed" => ResourceColor("Danger", Colors.Red),
            _ => ResourceColor("Accent", Colors.DodgerBlue)
        };
        var stack = new VerticalStackLayout { Spacing = 3 };
        AddTitle(stack, block.Title);
        stack.Children.Add(new Label { Text = block.Text ?? block.Status ?? "Status", FontSize = 12, TextColor = ResourceColor("TextPrimary", Colors.Black) });
        return new Border
        {
            Padding = 11, BackgroundColor = color.WithAlpha(0.10f), Stroke = color.WithAlpha(0.45f), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = stack
        };
    }

    private View BuildKeyValue(AgentUiBlock block)
    {
        var stack = new VerticalStackLayout { Spacing = 7 };
        AddTitle(stack, block.Title);
        foreach (var pair in block.Values ?? Array.Empty<AgentUiKeyValue>())
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            row.Add(new Label { Text = pair.Key, FontSize = 11, TextColor = ResourceColor("TextSecondary", Colors.Gray) }, 0, 0);
            row.Add(new Label { Text = pair.Value, FontSize = 11, HorizontalTextAlignment = TextAlignment.End, TextColor = ResourceColor("TextPrimary", Colors.Black) }, 1, 0);
            stack.Children.Add(row);
        }
        return Wrap(stack);
    }

    private View BuildList(AgentUiBlock block, bool checklist)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        AddTitle(stack, block.Title);
        foreach (var item in block.Items ?? Array.Empty<string>())
            stack.Children.Add(new Label { Text = checklist ? "◦  " + item : "•  " + item, FontSize = 12, TextColor = ResourceColor("TextPrimary", Colors.Black) });
        return Wrap(stack);
    }

    private View BuildProgress(AgentUiBlock block)
    {
        var stack = new VerticalStackLayout { Spacing = 5 };
        AddTitle(stack, block.Title);
        stack.Children.Add(new Label { Text = block.Text ?? "Em andamento", FontSize = 11, TextColor = ResourceColor("TextSecondary", Colors.Gray) });
        return Wrap(stack);
    }

    private View BuildAction(AgentUiBlock block, Func<string, Task>? onAction)
    {
        var button = new Button
        {
            Text = block.Title ?? block.Text ?? "Abrir",
            FontSize = 12,
            BackgroundColor = ResourceColor("AccentStrong", Colors.DodgerBlue),
            TextColor = Colors.White,
            CornerRadius = 12
        };
        if (onAction is null) button.IsEnabled = false;
        else button.Clicked += async (_, _) => await onAction(block.ActionId!);
        return button;
    }

    private View Wrap(View content) => new Border
    {
        Padding = 12,
        BackgroundColor = ResourceColor("Surface", Colors.White),
        Stroke = ResourceColor("Border", Colors.LightGray),
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
        Content = content
    };

    private View BuildError(string message) => new Label
    {
        Text = message,
        FontSize = 11,
        TextColor = ResourceColor("Danger", Colors.Red)
    };

    private static void AddTitle(VerticalStackLayout stack, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
            stack.Children.Add(new Label { Text = title, FontSize = 12, FontAttributes = FontAttributes.Bold });
    }

    private static Color ResourceColor(string key, Color fallback)
    {
        var resources = Application.Current?.Resources;
        return resources is not null && resources.TryGetValue(key, out var value) && value is Color color
            ? color : fallback;
    }
}
