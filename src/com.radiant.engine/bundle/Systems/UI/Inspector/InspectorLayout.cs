using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    /// <summary> Places a single new window in the first available spot without moving existing windows. </summary>
    private void PlaceNewWindow(WindowData NewWindow)
    {
        if (!NewWindow.AutoPosition)
        {
            int windowHeight = ComputeWindowHeight(NewWindow);
            NewWindow.Position = new Vector2(
                (Renderer.VirtualWidth / UIScale - NewWindow.Size.X) / 2,
                (Renderer.VirtualHeight / UIScale - windowHeight) / 2);
            return;
        }

        int newWidth = (int)NewWindow.Size.X;
        int newHeight = ComputeWindowHeight(NewWindow);
        float screenW = Renderer.VirtualWidth / UIScale;
        float screenH = Renderer.VirtualHeight / UIScale - AutoLayoutGap;
        float startY = MenuBarHeight + AutoLayoutGap;

        var existing = new List<Rectangle>();
        foreach (var window in Windows.Values)
        {
            if (window == NewWindow || !window.Visible) continue;
            existing.Add(new Rectangle((int)window.Position.X, (int)window.Position.Y, (int)window.Size.X, ComputeWindowHeight(window)));
        }

        var tryX = new List<float> { AutoLayoutGap };
        foreach (var rect in existing)
            tryX.Add(rect.Right + AutoLayoutGap);
        tryX.Sort();

        foreach (float candidateX in tryX)
        {
            if (candidateX + newWidth > screenW) continue;

            var tryY = new List<float> { startY };
            foreach (var rect in existing)
                tryY.Add(rect.Bottom + AutoLayoutGap);
            tryY.Sort();

            foreach (float candidateY in tryY)
            {
                if (candidateY + newHeight > screenH) continue;

                var candidate = new Rectangle((int)candidateX, (int)candidateY, newWidth, newHeight);
                bool overlaps = false;
                foreach (var rect in existing)
                {
                    if (candidate.Intersects(rect)) { overlaps = true; break; }
                }

                if (!overlaps)
                {
                    NewWindow.Position = new Vector2(candidateX, candidateY);
                    return;
                }
            }
        }

        float fallbackX = MathHelper.Clamp(AutoLayoutGap, 0, Math.Max(0, screenW - newWidth));
        float fallbackY = MathHelper.Clamp(startY, startY, Math.Max(startY, screenH - newHeight));
        int leastOverlap = int.MaxValue;

        foreach (float candidateX in tryX)
        {
            float clampedX = MathHelper.Clamp(candidateX, 0, Math.Max(0, screenW - newWidth));
            var candidate = new Rectangle((int)clampedX, (int)startY, newWidth, newHeight);
            int count = 0;
            foreach (var rect in existing)
            {
                if (candidate.Intersects(rect)) count++;
            }
            if (count < leastOverlap)
            {
                leastOverlap = count;
                fallbackX = clampedX;
                fallbackY = startY;
            }
        }

        NewWindow.Position = new Vector2(fallbackX, fallbackY);
    }

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
        float maxY = Renderer.VirtualHeight / UIScale - 64;

        foreach (var window in ordered)
        {
            if (!window.Visible || !window.AutoPosition) continue;

            int windowHeight = ComputeWindowHeight(window);
            if (currentY + windowHeight > maxY && currentY > MenuBarHeight + AutoLayoutGap)
            {
                columnX += DefaultWindowWidth + AutoLayoutGap;
                currentY = MenuBarHeight + AutoLayoutGap;
            }

            window.Position = new Vector2(columnX, currentY);
            currentY += windowHeight + AutoLayoutGap;
        }

        foreach (var window in ordered)
        {
            if (window.Visible || !window.AutoPosition) continue;

            int windowHeight = ComputeWindowHeight(window);
            if (currentY + windowHeight > maxY && currentY > MenuBarHeight + AutoLayoutGap)
            {
                columnX += DefaultWindowWidth + AutoLayoutGap;
                currentY = MenuBarHeight + AutoLayoutGap;
            }

            window.Position = new Vector2(columnX, currentY);
            currentY += windowHeight + AutoLayoutGap;
        }

        foreach (var window in ordered)
        {
            if (window.AutoPosition) continue;
            int windowHeight = ComputeWindowHeight(window);
            window.Position = new Vector2(
                (Renderer.VirtualWidth / UIScale - window.Size.X) / 2,
                (Renderer.VirtualHeight / UIScale - windowHeight) / 2);
        }
    }

    private int GetWidgetHeight(Widget Widget, int ContentWidth) => Widget.Type switch
    {
        WidgetType.Slider => WidgetHeight + 18,
        WidgetType.Label => MeasureWrappedHeight(Widget.Text, ContentWidth),
        WidgetType.ListBox => Widget.ListBoxHeight,
        _ => WidgetHeight
    };

    private bool IsNextWidgetInline(WindowData Window, int CurrentIndex)
    {
        for (int j = CurrentIndex + 1; j < Window.Widgets.Count; j++)
        {
            if (!Window.Widgets[j].Visible) continue;
            return Window.Widgets[j].InlineRatio > 0;
        }
        return false;
    }

    /// <summary> Calculates the total pixel height of a window based on its visible widgets. </summary>
    private int ComputeWindowHeight(WindowData Window)
    {
        if (Window.Resizable && Window.ResizedHeight > 0)
            return (int)Window.ResizedHeight;

        int contentWidth = (int)Window.Size.X - Padding * 2;
        int height = TitleBarHeight + WidgetSpacing;
        int inlineMaxH = 0;
        bool inRow = false;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible) continue;

            int widgetH = GetWidgetHeight(widget, contentWidth);

            if (widget.InlineRatio > 0)
            {
                inlineMaxH = Math.Max(inlineMaxH, widgetH);
                inRow = true;
                if (!IsNextWidgetInline(Window, i))
                {
                    height += inlineMaxH + WidgetSpacing;
                    inlineMaxH = 0;
                    inRow = false;
                }
                continue;
            }

            if (inRow)
            {
                height += inlineMaxH + WidgetSpacing;
                inlineMaxH = 0;
                inRow = false;
            }

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

        if (inRow) height += inlineMaxH + WidgetSpacing;

        return height + Padding;
    }

    private int ComputeDynamicListBoxHeight(WindowData Window)
    {
        int contentWidth = (int)Window.Size.X - Padding * 2;
        int fixedHeight = TitleBarHeight + WidgetSpacing + Padding;
        int listBoxCount = 0;
        int inlineMaxH = 0;
        bool inRow = false;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible) continue;

            if (widget.Type == WidgetType.ListBox)
            {
                listBoxCount++;
                fixedHeight += WidgetSpacing;
                continue;
            }

            int widgetH = GetWidgetHeight(widget, contentWidth);

            if (widget.InlineRatio > 0)
            {
                inlineMaxH = Math.Max(inlineMaxH, widgetH);
                inRow = true;
                if (!IsNextWidgetInline(Window, i))
                {
                    fixedHeight += inlineMaxH + WidgetSpacing;
                    inlineMaxH = 0;
                    inRow = false;
                }
                continue;
            }

            if (inRow)
            {
                fixedHeight += inlineMaxH + WidgetSpacing;
                inlineMaxH = 0;
                inRow = false;
            }

            bool tightLabel = widget.Type == WidgetType.Label && !widget.Section;
            bool nextIsTightLabel = false;
            for (int j = i + 1; j < Window.Widgets.Count; j++)
            {
                if (!Window.Widgets[j].Visible) continue;
                nextIsTightLabel = Window.Widgets[j].Type == WidgetType.Label && !Window.Widgets[j].Section;
                break;
            }

            fixedHeight += widgetH + (tightLabel && nextIsTightLabel ? LabelSpacing : WidgetSpacing);
        }

        if (inRow) fixedHeight += inlineMaxH + WidgetSpacing;

        int remaining = (int)Window.ResizedHeight - fixedHeight;
        if (listBoxCount > 0) remaining /= listBoxCount;
        return Math.Max(80, remaining);
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

        int dynamicListBoxHeight = (Window.Resizable && Window.ResizedHeight > 0)
            ? ComputeDynamicListBoxHeight(Window) : -1;

        Window.TitleBarBounds = new Rectangle(posX, posY, windowWidth, TitleBarHeight);
        Window.CloseBounds = new Rectangle(posX + windowWidth - CloseButtonWidth - 6, posY + (TitleBarHeight - CloseButtonSize) / 2, CloseButtonWidth, CloseButtonSize);

        int contentWidth = windowWidth - Padding * 2;
        int widgetY = posY + TitleBarHeight + WidgetSpacing;
        int inlineX = posX + Padding;
        int inlineMaxH = 0;
        bool inRow = false;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible) continue;

            int widgetH = (widget.Type == WidgetType.ListBox && dynamicListBoxHeight > 0)
                ? dynamicListBoxHeight : GetWidgetHeight(widget, contentWidth);

            if (widget.InlineRatio > 0)
            {
                if (!inRow) { inlineX = posX + Padding; inlineMaxH = 0; inRow = true; }
                int widgetWidth = (int)(contentWidth * widget.InlineRatio) - InlineGap / 2;
                widget.Bounds = new Rectangle(inlineX, widgetY, widgetWidth, widgetH);
                Window.Widgets[i] = widget;
                inlineX += widgetWidth + InlineGap;
                inlineMaxH = Math.Max(inlineMaxH, widgetH);

                if (!IsNextWidgetInline(Window, i))
                {
                    widgetY += inlineMaxH + WidgetSpacing;
                    inRow = false;
                }
                continue;
            }

            if (inRow)
            {
                widgetY += inlineMaxH + WidgetSpacing;
                inRow = false;
            }

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

        if (inRow) widgetY += inlineMaxH + WidgetSpacing;

        int totalHeight = (Window.Resizable && Window.ResizedHeight > 0)
            ? (int)Window.ResizedHeight : widgetY - posY + Padding;
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
