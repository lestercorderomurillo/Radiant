using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    /// <summary> Arranges all windows in columns, wrapping to a new column when vertical space runs out. </summary>
    private void AutoPositionAll()
    {
        var ordered = new List<WindowData>(Windows.Values);
        ordered.Sort((a, b) =>
        {
            int order = a.LayoutOrder.CompareTo(b.LayoutOrder);
            return order != 0 ? order : a.CreationIndex.CompareTo(b.CreationIndex);
        });

        float columnX = AutoLayoutGap;
        float currentY = MenuBarHeight + AutoLayoutGap;
        Vector2 lastVisiblePos = new Vector2(columnX, currentY);

        foreach (var window in ordered)
        {
            if (!window.Visible)
            {
                window.Position = lastVisiblePos;
                continue;
            }

            int windowHeight = ComputeWindowHeight(window);

            float maxY = Renderer.VirtualHeight / UIScale - 64;
            if (currentY + windowHeight > maxY && currentY > MenuBarHeight + AutoLayoutGap)
            {
                columnX += DefaultWindowWidth + AutoLayoutGap;
                currentY = MenuBarHeight + AutoLayoutGap;
            }

            window.Position = new Vector2(columnX, currentY);
            lastVisiblePos = window.Position;
            currentY += windowHeight + AutoLayoutGap;
        }
    }

    /// <summary> Calculates the total pixel height of a window based on its visible widgets. </summary>
    private int ComputeWindowHeight(WindowData Window)
    {
        int contentWidth = (int)Window.Size.X - Padding * 2;
        int height = TitleBarHeight + WidgetSpacing;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible) continue;

            int widgetH = widget.Type switch
            {
                WidgetType.Slider => WidgetHeight + 25,
                WidgetType.Label => MeasureWrappedHeight(widget.Text, contentWidth),
                _ => WidgetHeight
            };

            bool tightLabel = widget.Type == WidgetType.Label && !widget.Section;
            bool nextIsTightLabel = false;
            for (int j = i + 1; j < Window.Widgets.Count; j++)
            {
                if (!Window.Widgets[j].Visible) continue;
                nextIsTightLabel = Window.Widgets[j].Type == WidgetType.Label && !Window.Widgets[j].Section;
                break;
            }

            height += widgetH + (tightLabel && nextIsTightLabel ? LabelSpacing : WidgetSpacing);
        }

        return height + Padding;
    }

    /// <summary> Recomputes layout bounds for all visible windows in render order. </summary>
    private void ComputeAllLayouts()
    {
        foreach (var window in RenderOrder)
        {
            if (!window.Visible) continue;
            ComputeLayout(window);
        }
    }

    /// <summary> Computes title bar, close button, widget, and overall bounds for a single window. </summary>
    private void ComputeLayout(WindowData Window)
    {
        int posX = (int)Window.Position.X;
        int posY = (int)Window.Position.Y;
        int windowWidth = (int)Window.Size.X;

        Window.TitleBarBounds = new Rectangle(posX, posY, windowWidth, TitleBarHeight);
        Window.CloseBounds = new Rectangle(posX + windowWidth - CloseButtonWidth - 6, posY + (TitleBarHeight - CloseButtonSize) / 2, CloseButtonWidth, CloseButtonSize);

        int contentWidth = windowWidth - Padding * 2;
        int widgetY = posY + TitleBarHeight + WidgetSpacing;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible) continue;

            int widgetH = widget.Type switch
            {
                WidgetType.Slider => WidgetHeight + 25,
                WidgetType.Label => MeasureWrappedHeight(widget.Text, contentWidth),
                _ => WidgetHeight
            };

            widget.Bounds = new Rectangle(posX + Padding, widgetY, contentWidth, widgetH);
            Window.Widgets[i] = widget;

            bool tightLabel = widget.Type == WidgetType.Label && !widget.Section;
            bool nextIsTightLabel = false;
            for (int j = i + 1; j < Window.Widgets.Count; j++)
            {
                if (!Window.Widgets[j].Visible) continue;
                nextIsTightLabel = Window.Widgets[j].Type == WidgetType.Label && !Window.Widgets[j].Section;
                break;
            }

            widgetY += widgetH + (tightLabel && nextIsTightLabel ? LabelSpacing : WidgetSpacing);
        }

        int totalHeight = widgetY - posY + Padding;
        Window.WindowBounds = new Rectangle(posX, posY, windowWidth, totalHeight);
    }

    /// <summary> Measures the pixel height of text that word-wraps within the given width. </summary>
    private int MeasureWrappedHeight(string Text, int AvailableWidth)
    {
        int maxWidth = AvailableWidth - 8;
        if (MeasureText(Text).X <= maxWidth)
            return (int)MeasureText(Text).Y;

        string[] words = Text.Split(' ');
        float spaceWidth = MeasureText(" ").X;
        int lines = 1;
        float lineWidth = 0;

        for (int i = 0; i < words.Length; i++)
        {
            float wordWidth = MeasureText(words[i]).X;
            float addWidth = lineWidth == 0 ? wordWidth : spaceWidth + wordWidth;

            if (lineWidth + addWidth > maxWidth && lineWidth > 0)
            {
                lines++;
                lineWidth = wordWidth;
            }
            else
            {
                lineWidth += addWidth;
            }
        }

        return (int)(lines * LineHeight + 8);
    }

    /// <summary> Converts screen-space pixel coordinates to virtual-space coordinates adjusted for UI scale. </summary>
    private Vector2 ScreenToVirtual(Vector2 ScreenPos)
    {
        return new Vector2(
            ScreenPos.X * (Renderer.VirtualWidth / Renderer.ScreenWidth) / UIScale,
            ScreenPos.Y * (Renderer.VirtualHeight / Renderer.ScreenHeight) / UIScale);
    }

    /// <summary> Calculates the UI scale factor based on screen height, snapped to 0.5 increments. </summary>
    private float ComputeAutoScale()
    {
        float scale = 0.5f + 1080f / Renderer.ScreenHeight;
        scale = MathF.Round(scale * 2f) / 2f;
        return MathHelper.Clamp(scale, 0.5f, 2.5f);
    }
}
