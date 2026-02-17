using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private RenderTarget2D BlurRT_A;
    private RenderTarget2D BlurRT_B;
    private RenderTarget2D BlurResult;

    private const int BlurDownscale = 4;
    private const int BlurPasses = 5;
    private const float GlassTintOpacity = 0.45f;
    private const float GlassTintBrightOpacity = 0.40f;
    private const float GlassBrightnessThreshold = 0.5f;
    private const float ShadowOffsetY = 3f;
    private const float ShadowSpreadSize = 12f;
    private const float ShadowAlpha = 0.25f;

    /// <summary> Renders all visible Inspector windows with blur, shadows, and glass effects. </summary>
    public override void LateRender()
    {
        if (!GlobalVisible) return;

        ComputeAllLayouts();

        var scale = Matrix.CreateScale(
            Renderer.ScreenWidth / Renderer.VirtualWidth * UIScale,
            Renderer.ScreenHeight / Renderer.VirtualHeight * UIScale,
            1f);

        var currentMouse = Mouse.GetState();
        var virtualMouse = ScreenToVirtual(new Vector2(currentMouse.X, currentMouse.Y));

        string hoveredWindowId = null;
        bool menuBlocking = OpenMenuId != null || MenuBarBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y);
        if (!Dragging && !DraggingSlider && OpenDropdownWindowId == null && !menuBlocking)
        {
            for (int i = RenderOrder.Count - 1; i >= 0; i--)
            {
                var win = RenderOrder[i];
                if (!win.Visible) continue;
                if (win.WindowBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                {
                    hoveredWindowId = win.Id;
                    break;
                }
            }
        }

        UpdateBlurPipeline();

        foreach (var window in RenderOrder)
        {
            if (!window.Visible) continue;

            DrawWindowShadow(window);

            if (BlurResult != null)
                DrawWindowBlurQuad(window);

            Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: scale);
            DrawWindow(window, virtualMouse, window.Id != hoveredWindowId);
            Renderer.EndDraw();
        }

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: scale);
        DrawDropdownPopup(virtualMouse);
        Renderer.EndDraw();

        if (BlurResult != null)
            DrawBlurQuad(MenuBarBounds, 0);

        if (OpenMenuId != null)
        {
            DrawShadowQuad(OpenMenuDropdownBounds, CornerRadius);
            if (BlurResult != null)
                DrawBlurQuad(OpenMenuDropdownBounds, CornerRadius);
        }

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: scale);
        DrawMenuBar(virtualMouse);
        DrawMenuDropdown(virtualMouse);
        Renderer.EndDraw();
    }

    /// <summary> Measures text dimensions using the Inter font at the default size. </summary>
    private Vector2 MeasureText(string text) => Renderer.MeasureString("Inter", FontSize, text);

    /// <summary> Truncates text with an ellipsis if it exceeds the given pixel width. </summary>
    private string TruncateText(string text, float maxWidth)
    {
        if (MeasureText(text).X <= maxWidth) return text;
        float ellipsisWidth = MeasureText("...").X;
        for (int i = text.Length - 1; i > 0; i--)
        {
            if (MeasureText(text[..i]).X + ellipsisWidth <= maxWidth)
                return text[..i] + "...";
        }
        return "...";
    }

    /// <summary> Draws text using the Inter regular font at the default size. </summary>
    private void DrawText(string text, Vector2 position, Color color)
        => Renderer.DrawString("Inter", FontSize, text, position, color);

    /// <summary> Draws text using the Inter bold font at the default size. </summary>
    private void DrawTextBold(string text, Vector2 position, Color color)
        => Renderer.DrawString("Inter-Bold", FontSize, text, position, color);

    /// <summary> Draws a complete window: background, title bar, close button, and all widgets. </summary>
    private void DrawWindow(WindowData Window, Vector2 Mouse, bool HoverBlocked)
    {
        Renderer.DrawRoundedRect(Window.WindowBounds, BlurResult != null ? GlassTint(WindowBg) : WindowBg, CornerRadius);

        bool titleHovered = !HoverBlocked && Window.TitleBarBounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Color titleBg = titleHovered ? TitleBarHover : TitleBarColor;
        if (BlurResult != null) titleBg = GlassTint(titleBg);
        Renderer.DrawRoundedRect(Window.TitleBarBounds, titleBg, CornerRadius, RoundedCorners.Top);

        float titleTextHeight = Renderer.MeasureString("Inter-Bold", FontSize, Window.Title).Y;
        var titlePos = new Vector2(Window.TitleBarBounds.X + Padding, Window.TitleBarBounds.Y + (TitleBarHeight - titleTextHeight) / 2);
        DrawTextBold(Window.Title, titlePos, TextColor);

        bool closeHovered = !HoverBlocked && Window.CloseBounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawRoundedRect(Window.CloseBounds, closeHovered ? CloseHover : CloseColor, CornerRadius);
        const float closeFontSize = FontSize * 0.7f;
        var closeTextSize = Renderer.MeasureString("Inter-Bold", closeFontSize, "X");
        var closeTextPos = new Vector2(
            Window.CloseBounds.X + (Window.CloseBounds.Width - closeTextSize.X) / 2,
            Window.CloseBounds.Y + (Window.CloseBounds.Height - closeTextSize.Y) / 2 - 2);
        Renderer.DrawString("Inter-Bold", closeFontSize, "X", closeTextPos, CloseText);

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];
            if (!widget.Visible) continue;

            switch (widget.Type)
            {
                case WidgetType.Label: DrawLabel(widget); break;
                case WidgetType.Button: DrawButton(widget, Mouse, HoverBlocked); break;
                case WidgetType.Toggle: DrawToggle(widget); break;
                case WidgetType.Slider: DrawSlider(widget); break;
                case WidgetType.Dropdown: DrawDropdown(widget, Mouse, HoverBlocked, Window.Id); break;
            }
        }
    }

    /// <summary> Draws a label widget with section header styling or word-wrapped body text. </summary>
    private void DrawLabel(Widget Widget)
    {
        Color labelColor = Widget.Section ? TextColor : LabelDim;

        if (Widget.Section)
        {
            var solid = Renderer.GetSolidTexture(Color.White);
            var textPos = new Vector2(Widget.Bounds.X + 4, Widget.Bounds.Y + (Widget.Bounds.Height - LineHeight) / 2);
            DrawTextBold(Widget.Text, textPos, labelColor);

            int textRight = (int)(textPos.X + MeasureText(Widget.Text).X) + 8;
            int lineY = Widget.Bounds.Y + Widget.Bounds.Height / 2;
            var lineRect = new Rectangle(textRight, lineY, Widget.Bounds.Right - textRight, 1);
            Renderer.DrawSprite(solid, lineRect, TextColor, 0.20f);
            return;
        }

        int maxWidth = Widget.Bounds.Width - 8;
        if (MeasureText(Widget.Text).X <= maxWidth)
        {
            var textPos = new Vector2(Widget.Bounds.X + 4, Widget.Bounds.Y + (Widget.Bounds.Height - LineHeight) / 2);
            DrawText(Widget.Text, textPos, labelColor);
            return;
        }

        string[] words = Widget.Text.Split(' ');
        float currentY = Widget.Bounds.Y + 4;
        string currentLine = "";

        for (int i = 0; i < words.Length; i++)
        {
            string testLine = currentLine.Length == 0 ? words[i] : currentLine + " " + words[i];
            if (MeasureText(testLine).X > maxWidth && currentLine.Length > 0)
            {
                DrawText(currentLine, new Vector2(Widget.Bounds.X + 4, currentY), labelColor);
                currentY += LineHeight;
                currentLine = words[i];
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (currentLine.Length > 0)
            DrawText(currentLine, new Vector2(Widget.Bounds.X + 4, currentY), labelColor);
    }

    /// <summary> Draws a button widget with centered text and hover highlight. </summary>
    private void DrawButton(Widget Widget, Vector2 Mouse, bool HoverBlocked)
    {
        bool hovered = !HoverBlocked && Widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawRoundedRect(Widget.Bounds, hovered ? ButtonHover : ButtonColor, CornerRadius);

        var textSize = MeasureText(Widget.Text);
        var textPos = new Vector2(
            Widget.Bounds.X + (Widget.Bounds.Width - textSize.X) / 2,
            Widget.Bounds.Y + (Widget.Bounds.Height - textSize.Y) / 2);
        DrawText(Widget.Text, textPos, TextColor);
    }

    /// <summary> Draws a toggle checkbox widget with a filled inner square when active. </summary>
    private void DrawToggle(Widget Widget)
    {
        int boxY = Widget.Bounds.Y + (Widget.Bounds.Height - ToggleBoxSize) / 2;
        var boxRect = new Rectangle(Widget.Bounds.X, boxY, ToggleBoxSize, ToggleBoxSize);
        var solid = Renderer.GetSolidTexture(Color.White);
        Renderer.DrawSprite(solid, boxRect, Widget.ToggleValue ? ToggleOn : ToggleOff);

        if (Widget.ToggleValue)
        {
            int inset = 5;
            var innerRect = new Rectangle(boxRect.X + inset, boxRect.Y + inset, boxRect.Width - inset * 2, boxRect.Height - inset * 2);
            Renderer.DrawSprite(solid, innerRect, TextColor);
        }

        var textPos = new Vector2(Widget.Bounds.X + ToggleBoxSize + 8, Widget.Bounds.Y + (Widget.Bounds.Height - LineHeight) / 2);
        DrawText(Widget.Text, textPos, TextColor);
    }

    /// <summary> Draws a slider widget with value text, track, fill bar, and circular handle. </summary>
    private void DrawSlider(Widget Widget)
    {
        string valueText = $"{Widget.Text}: {Widget.SliderValue:F2}";
        var textPos = new Vector2(Widget.Bounds.X + 4, Widget.Bounds.Y + 2);
        DrawText(valueText, textPos, TextColor);

        int trackY = (int)(Widget.Bounds.Y + LineHeight + 8);
        int trackLeft = Widget.Bounds.X + 4;
        int trackWidth = Widget.Bounds.Width - 8;
        var trackRect = new Rectangle(trackLeft, trackY, trackWidth, SliderTrackHeight);
        Renderer.DrawRoundedRect(trackRect, SliderTrack, CornerRadius);

        float range = Widget.SliderMax - Widget.SliderMin;
        float normalizedValue = range > 0 ? (Widget.SliderValue - Widget.SliderMin) / range : 0;
        int fillWidth = (int)(trackWidth * normalizedValue);

        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(trackLeft, trackY, fillWidth, SliderTrackHeight);
            Renderer.DrawRoundedRect(fillRect, SliderFill, CornerRadius);
        }

        int handleX = trackLeft + fillWidth - SliderHandleSize / 2;
        int handleY = trackY + SliderTrackHeight / 2 - SliderHandleSize / 2;
        var handleRect = new Rectangle(handleX, handleY, SliderHandleSize, SliderHandleSize);
        Renderer.DrawSprite(Renderer.GetCircleTexture(64), handleRect, SliderHandle);
    }

    /// <summary> Draws a dropdown widget showing the selected option with a triangle indicator. </summary>
    private void DrawDropdown(Widget Widget, Vector2 Mouse, bool HoverBlocked, string WindowId)
    {
        bool hovered = !Widget.Disabled && !HoverBlocked && Widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Color bgColor = Widget.Disabled ? Dim(ButtonColor) : (hovered ? ButtonHover : ButtonColor);
        bool isOpen = OpenDropdownWindowId == WindowId && OpenDropdownWidgetId == Widget.Id;
        Renderer.DrawRoundedRect(Widget.Bounds, bgColor, CornerRadius, isOpen ? RoundedCorners.Top : RoundedCorners.All);

        string selectedLabel = Widget.DropdownOptions != null && Widget.DropdownSelected < Widget.DropdownOptions.Length
            ? Widget.DropdownOptions[Widget.DropdownSelected] : "?";
        string displayText = $"{Widget.Text}: {selectedLabel}";

        const int triSize = 7;
        float availableWidth = Widget.Bounds.Width - Padding * 2 - triSize - 4;
        displayText = TruncateText(displayText, availableWidth);

        Color textColor = Widget.Disabled ? Dim(LabelDim) : TextColor;
        var textSize = MeasureText(displayText);
        var textPos = new Vector2(
            Widget.Bounds.X + (Widget.Bounds.Width - textSize.X - triSize - 4) / 2,
            Widget.Bounds.Y + (Widget.Bounds.Height - textSize.Y) / 2);
        DrawText(displayText, textPos, textColor);

        int triX = Widget.Bounds.Right - Padding - triSize;
        int triY = Widget.Bounds.Y + (Widget.Bounds.Height - triSize) / 2 + 1;
        Renderer.DrawSprite(Renderer.GetTriangleTexture(triSize * 4), new Rectangle(triX, triY, triSize, triSize), Widget.Disabled ? Dim(LabelDim) : LabelDim);
    }

    /// <summary> Dims a color by halving its alpha, used for disabled widget rendering. </summary>
    private static Color Dim(Color color) => new(color.R, color.G, color.B, (byte)(color.A / 2));

    /// <summary> Draws the open dropdown popup with scrollable options and a scrollbar. </summary>
    private void DrawDropdownPopup(Vector2 Mouse)
    {
        if (OpenDropdownWindowId == null) return;
        if (!Windows.TryGetValue(OpenDropdownWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(OpenDropdownWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        if (widget.DropdownOptions == null) return;

        Renderer.DrawRoundedRect(OpenDropdownPopupBounds, WindowBg, CornerRadius, RoundedCorners.Bottom);

        int visibleCount = Math.Min(widget.DropdownOptions.Length - DropdownScrollOffset, MaxVisibleDropdownItems);
        bool scrollable = widget.DropdownOptions.Length > MaxVisibleDropdownItems;

        for (int i = 0; i < visibleCount; i++)
        {
            int optionIndex = i + DropdownScrollOffset;
            var optionRect = new Rectangle(OpenDropdownPopupBounds.X, OpenDropdownPopupBounds.Y + i * WidgetHeight, OpenDropdownPopupBounds.Width, WidgetHeight);
            bool optionHovered = optionRect.Contains((int)Mouse.X, (int)Mouse.Y);
            bool isSelected = optionIndex == widget.DropdownSelected;

            RoundedCorners optionCorners = visibleCount == 1 ? RoundedCorners.Bottom :
                i == visibleCount - 1 ? RoundedCorners.Bottom : RoundedCorners.None;
            Renderer.DrawRoundedRect(optionRect, ButtonColor, CornerRadius, optionCorners);

            if (isSelected || optionHovered)
            {
                bool isLast = i == visibleCount - 1;
                int highlightWidth = scrollable ? OpenDropdownPopupBounds.Width - 10 : OpenDropdownPopupBounds.Width;
                var highlightRect = new Rectangle(OpenDropdownPopupBounds.X, optionRect.Y, highlightWidth, WidgetHeight);
                RoundedCorners highlightCorners = isLast ? (scrollable ? RoundedCorners.BL : RoundedCorners.Bottom) : RoundedCorners.None;
                Renderer.DrawRoundedRect(highlightRect, isSelected ? SliderFill : ButtonHover, CornerRadius, highlightCorners);
            }

            string optionText = TruncateText(widget.DropdownOptions[optionIndex], optionRect.Width - Padding * 2);
            var optionTextSize = MeasureText(optionText);
            var optionTextPos = new Vector2(optionRect.X + Padding, optionRect.Y + (optionRect.Height - optionTextSize.Y) / 2);
            DrawText(optionText, optionTextPos, TextColor);
        }

        if (scrollable)
        {
            int thumbWidth = 4;
            int thumbMargin = 3;
            int thumbX = OpenDropdownPopupBounds.Right - thumbMargin - thumbWidth;
            float thumbRatio = (float)MaxVisibleDropdownItems / DropdownTotalOptions;
            int thumbHeight = Math.Max(8, (int)(OpenDropdownPopupBounds.Height * thumbRatio));
            int scrollRange = OpenDropdownPopupBounds.Height - thumbHeight;
            int maxScroll = Math.Max(1, DropdownTotalOptions - MaxVisibleDropdownItems);
            int thumbY = OpenDropdownPopupBounds.Y + (int)(scrollRange * ((float)DropdownScrollOffset / maxScroll));
            Renderer.DrawRoundedRect(new Rectangle(thumbX, thumbY, thumbWidth, thumbHeight), SliderFill, thumbWidth / 2);
        }
    }

    /// <summary> Runs the Kawase blur pipeline on the scene RT to produce the frosted glass texture. </summary>
    private void UpdateBlurPipeline()
    {
        if (Renderer.SceneRT == null)
        {
            BlurResult = null;
            return;
        }

        int blurWidth = Math.Max(1, Renderer.ScreenWidth / BlurDownscale);
        int blurHeight = Math.Max(1, Renderer.ScreenHeight / BlurDownscale);

        if (BlurRT_A == null || BlurRT_A.Width != blurWidth || BlurRT_A.Height != blurHeight)
        {
            BlurRT_A?.Dispose();
            BlurRT_B?.Dispose();
            BlurRT_A = Renderer.CreateRenderTarget(blurWidth, blurHeight);
            BlurRT_B = Renderer.CreateRenderTarget(blurWidth, blurHeight);
        }

        Renderer.SetTarget(BlurRT_A).Clear(Color.Black);
        Renderer.Blit(Renderer.SceneRT, BlendState.Opaque, SamplerState.LinearClamp);

        var texelSize = new Vector2(1f / blurWidth, 1f / blurHeight);
        Renderer
            .Reset()
            .SetShader("GlassBlur")
            .SetTechnique("Blur")
            .Configure(BlendState.Opaque)
            .Configure(SamplerState.LinearClamp, 0);

        BlurResult = Renderer.PingPong(BlurRT_A, BlurRT_B, BlurPasses,
            (passIndex, input) =>
            {
                Renderer.SetParameter("InputTexture", input);
                Renderer.SetParameter("TexelSize", texelSize);
                Renderer.SetParameter("BlurOffset", (float)passIndex);
            },
            clearColor: Color.Black);
    }

    /// <summary> Draws the frosted glass background quad clipped to a window's rounded bounds. </summary>
    private void DrawWindowBlurQuad(WindowData Window) => DrawBlurQuad(Window.WindowBounds, CornerRadius);

    /// <summary> Draws a soft drop shadow beneath a window using the Shadow shader technique. </summary>
    private void DrawWindowShadow(WindowData Window) => DrawShadowQuad(Window.WindowBounds, CornerRadius);

    /// <summary> Draws a frosted glass blur quad for the given virtual-space bounds. </summary>
    private void DrawBlurQuad(Rectangle Bounds, float Radius)
    {
        float scaleX = UIScale * Renderer.VirtualToScreenScale.X;
        float scaleY = UIScale * Renderer.VirtualToScreenScale.Y;
        var rect = new Vector4(Bounds.X * scaleX, Bounds.Y * scaleY, Bounds.Width * scaleX, Bounds.Height * scaleY);

        Renderer
            .Reset()
            .SetShader("GlassBlur")
            .SetTechnique("RoundedBlit")
            .Configure(BlendState.AlphaBlend)
            .Configure(SamplerState.LinearClamp, 0)
            .SetParameter("InputTexture", BlurResult)
            .SetParameter("ScreenSize", Renderer.ScreenSize)
            .SetParameter("WindowRect", rect)
            .SetParameter("WindowRadius", Radius * Math.Min(scaleX, scaleY))
            .Draw()
            .Commit();
    }

    /// <summary> Draws a soft drop shadow for the given virtual-space bounds. </summary>
    private void DrawShadowQuad(Rectangle Bounds, float Radius)
    {
        float scaleX = UIScale * Renderer.VirtualToScreenScale.X;
        float scaleY = UIScale * Renderer.VirtualToScreenScale.Y;
        var rect = new Vector4(Bounds.X * scaleX, Bounds.Y * scaleY, Bounds.Width * scaleX, Bounds.Height * scaleY);

        Renderer
            .Reset()
            .SetShader("GlassBlur")
            .SetTechnique("Shadow")
            .Configure(BlendState.AlphaBlend)
            .SetParameter("ScreenSize", Renderer.ScreenSize)
            .SetParameter("WindowRect", rect)
            .SetParameter("WindowRadius", Radius * Math.Min(scaleX, scaleY))
            .SetParameter("ShadowOffset", new Vector2(0, ShadowOffsetY * scaleY))
            .SetParameter("ShadowSpread", ShadowSpreadSize * Math.Min(scaleX, scaleY))
            .SetParameter("ShadowOpacity", ShadowAlpha)
            .Draw()
            .Commit();
    }

    /// <summary>
    /// Premultiplied glass tint. Scales RGB and A by the same opacity factor so bright colors
    /// don't appear solid under BlendState.AlphaBlend (source blend = One).
    /// </summary>
    private static Color GlassTint(Color color)
    {
        float luminance = (0.299f * color.R + 0.587f * color.G + 0.114f * color.B) / 255f;
        float opacity = luminance > GlassBrightnessThreshold ? GlassTintBrightOpacity : GlassTintOpacity;
        return new Color(
            (byte)(color.R * opacity),
            (byte)(color.G * opacity),
            (byte)(color.B * opacity),
            (byte)(color.A * opacity));
    }
}
