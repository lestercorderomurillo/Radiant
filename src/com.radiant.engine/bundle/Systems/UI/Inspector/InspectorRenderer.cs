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
    private const float GlassTintOpacity = 0.50f;
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
        const float closeFontSize = FontSize * 0.8f;
        var closeTextSize = Renderer.MeasureString("Inter-Bold", closeFontSize, "X");
        var closeTextPos = new Vector2(
            Window.CloseBounds.X + (Window.CloseBounds.Width - closeTextSize.X) / 2,
            Window.CloseBounds.Y + (Window.CloseBounds.Height - closeTextSize.Y) / 2 - 2);
        Renderer.DrawString("Inter-Bold", closeFontSize, "X", closeTextPos, CloseText, bold: true);

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
                case WidgetType.TextInput: DrawTextInput(widget, Mouse, HoverBlocked, Window.Id); break;
                case WidgetType.ListBox: DrawListBox(widget); break;
                case WidgetType.Dropdown: DrawDropdown(widget, Mouse, HoverBlocked, Window.Id); break;
            }
        }

        if (Window.Resizable)
        {
            var solid = Renderer.GetSolidTexture(Color.White);
            Color gripColor = new Color(TextColor.R, TextColor.G, TextColor.B, (byte)160);
            int bx = Window.WindowBounds.Right - 7;
            int by = Window.WindowBounds.Bottom - 7;
            Renderer.DrawSprite(solid, new Rectangle(bx, by, 4, 4), gripColor);
            Renderer.DrawSprite(solid, new Rectangle(bx - 8, by, 4, 4), gripColor);
            Renderer.DrawSprite(solid, new Rectangle(bx, by - 8, 4, 4), gripColor);
            Renderer.DrawSprite(solid, new Rectangle(bx - 16, by, 4, 4), gripColor);
            Renderer.DrawSprite(solid, new Rectangle(bx - 8, by - 8, 4, 4), gripColor);
            Renderer.DrawSprite(solid, new Rectangle(bx, by - 16, 4, 4), gripColor);
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

    /// <summary> Draws a button widget with centered text/icon and hover highlight. </summary>
    private void DrawButton(Widget Widget, Vector2 Mouse, bool HoverBlocked)
    {
        bool hovered = !Widget.Disabled && !HoverBlocked && Widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Renderer.DrawRoundedRect(Widget.Bounds, hovered ? ButtonHover : ButtonColor, CornerRadius);

        if (string.IsNullOrEmpty(Widget.Text) || Widget.Text == "trash")
        {
            int iconSize = Math.Min(Widget.Bounds.Width, Widget.Bounds.Height) - 20;
            if (iconSize < 4) iconSize = 4;
            var iconTex = Widget.Text == "trash"
                ? Renderer.GetTrashTexture(iconSize * 4)
                : Renderer.GetSearchTexture(iconSize * 4);
            var iconRect = new Rectangle(
                Widget.Bounds.X + (Widget.Bounds.Width - iconSize) / 2,
                Widget.Bounds.Y + (Widget.Bounds.Height - iconSize) / 2,
                iconSize, iconSize);
            Renderer.DrawSprite(iconTex, iconRect, Widget.Text == "trash" ? CloseHover : TextColor);
            return;
        }

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
        Renderer.DrawRoundedRect(boxRect, Widget.ToggleValue ? ToggleOn : ToggleOff, 4);

        if (Widget.ToggleValue)
        {
            int checkSize = ToggleBoxSize - 4;
            var checkTex = Renderer.GetCheckmarkTexture(checkSize * 4);
            var checkRect = new Rectangle(boxRect.X + 2, boxRect.Y + 2, checkSize, checkSize);
            Renderer.DrawSprite(checkTex, checkRect, CloseText);
        }

        var textPos = new Vector2(Widget.Bounds.X + ToggleBoxSize + 10, Widget.Bounds.Y + (Widget.Bounds.Height - LineHeight) / 2);
        DrawText(Widget.Text, textPos, TextColor);
    }

    /// <summary> Draws a slider widget with value text, track, fill bar, and circular handle. </summary>
    private void DrawSlider(Widget Widget)
    {
        string valueText = $"{Widget.Text}: {Widget.SliderValue:F2}";
        var textPos = new Vector2(Widget.Bounds.X + 4, Widget.Bounds.Y + 2);
        DrawText(valueText, textPos, TextColor);

        int trackY = (int)(Widget.Bounds.Y + LineHeight + 24);
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

    /// <summary> Draws a text input field with search icon, placeholder, cursor, and clear button. </summary>
    private void DrawTextInput(Widget Widget, Vector2 Mouse, bool HoverBlocked, string WindowId)
    {
        bool focused = IsTextInputFocused(WindowId, Widget.Id);
        bool hovered = !HoverBlocked && Widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);

        var borderRect = new Rectangle(Widget.Bounds.X - 1, Widget.Bounds.Y - 1, Widget.Bounds.Width + 2, Widget.Bounds.Height + 2);
        byte borderAlpha = focused ? (byte)120 : (byte)50;
        float borderFactor = borderAlpha / 255f;
        var borderColor = focused
            ? new Color((byte)(SliderFill.R * borderFactor), (byte)(SliderFill.G * borderFactor), (byte)(SliderFill.B * borderFactor), borderAlpha)
            : new Color((byte)(TextColor.R * borderFactor), (byte)(TextColor.G * borderFactor), (byte)(TextColor.B * borderFactor), borderAlpha);
        Renderer.DrawRoundedRect(borderRect, borderColor, CornerRadius + 1);

        Color bgColor = hovered || focused ? ButtonHover : ButtonColor;
        Renderer.DrawRoundedRect(Widget.Bounds, bgColor, CornerRadius);

        int iconSize = Widget.Bounds.Height - 36;
        int iconPad = 12;
        var searchTex = Renderer.GetSearchTexture(iconSize * 4);
        var searchRect = new Rectangle(Widget.Bounds.X + iconPad, Widget.Bounds.Y + (Widget.Bounds.Height - iconSize) / 2, iconSize, iconSize);
        Renderer.DrawSprite(searchTex, searchRect, LabelDim);

        string value = Widget.TextInputValue ?? "";
        int leftPadding = iconPad + iconSize + 8;
        int rightPadding = Padding / 2 + 4;

        if (value.Length > 0)
        {
            int clearSize = iconSize;
            int clearX = Widget.Bounds.Right - iconPad - clearSize;
            int clearY = Widget.Bounds.Y + (Widget.Bounds.Height - clearSize) / 2;
            var clearBounds = new Rectangle(clearX, clearY, clearSize, clearSize);
            bool clearHovered = !HoverBlocked && clearBounds.Contains((int)Mouse.X, (int)Mouse.Y);
            const float clearFontSize = FontSize * 0.75f;
            var clearTextSize = Renderer.MeasureString("Inter-Bold", clearFontSize, "X");
            var clearTextPos = new Vector2(clearBounds.X + (clearBounds.Width - clearTextSize.X) / 2, clearBounds.Y + (clearBounds.Height - clearTextSize.Y) / 2);
            Renderer.DrawString("Inter-Bold", clearFontSize, "X", clearTextPos, clearHovered ? TextColor : LabelDim, bold: true);
            rightPadding = iconPad + clearSize + 8;
        }

        float maxTextWidth = Widget.Bounds.Width - leftPadding - rightPadding;

        if (value.Length == 0 && !focused)
        {
            string placeholder = Widget.TextInputPlaceholder ?? "";
            placeholder = TruncateText(placeholder, maxTextWidth);
            var placeholderSize = MeasureText(placeholder);
            var placeholderPos = new Vector2(Widget.Bounds.X + leftPadding, Widget.Bounds.Y + (Widget.Bounds.Height - placeholderSize.Y) / 2);
            DrawText(placeholder, placeholderPos, LabelDim);
            return;
        }

        string displayText = TruncateText(value, maxTextWidth);
        var textSize = MeasureText(displayText);
        var textPos = new Vector2(Widget.Bounds.X + leftPadding, Widget.Bounds.Y + (Widget.Bounds.Height - textSize.Y) / 2);
        DrawText(displayText, textPos, TextColor);

        if (focused && ((int)(CursorBlinkTimer / CursorBlinkRate) % 2 == 0))
        {
            int cursor = Math.Clamp(Widget.TextInputCursor, 0, value.Length);
            string beforeCursor = cursor <= displayText.Length ? displayText[..cursor] : displayText;
            float cursorX = textPos.X + MeasureText(beforeCursor).X;
            var solid = Renderer.GetSolidTexture(Color.White);
            var cursorRect = new Rectangle((int)cursorX, Widget.Bounds.Y + 10, 2, Widget.Bounds.Height - 20);
            Renderer.DrawSprite(solid, cursorRect, TextColor);
        }
    }

    /// <summary> Draws a list box with selectable items, scroll support, and selection highlights. </summary>
    private void DrawListBox(Widget Widget)
    {
        var borderRect = new Rectangle(Widget.Bounds.X - 1, Widget.Bounds.Y - 1, Widget.Bounds.Width + 2, Widget.Bounds.Height + 2);
        byte borderAlpha = 40;
        float borderFactor = borderAlpha / 255f;
        var borderColor = new Color((byte)(TextColor.R * borderFactor), (byte)(TextColor.G * borderFactor), (byte)(TextColor.B * borderFactor), borderAlpha);
        Renderer.DrawRoundedRect(borderRect, borderColor, CornerRadius + 1);
        Renderer.DrawRoundedRect(Widget.Bounds, ButtonColor, CornerRadius);

        int itemHeight = (int)(LineHeight + 4);
        int headerOffset = 0;

        if (Widget.ListBoxHeader != null)
        {
            int headerY = Widget.Bounds.Y + 4;
            var headerPos = new Vector2(Widget.Bounds.X + Padding, headerY);
            float headerMaxWidth = Widget.Bounds.Width - Padding * 2;
            string headerText = Widget.ListBoxHeader;
            int sepIdx = headerText.IndexOf("  |  ", StringComparison.Ordinal);
            if (sepIdx < 0)
            {
                DrawTextBold(TruncateText(headerText, headerMaxWidth), headerPos, LabelDim);
            }
            else
            {
                string headerPrimary = headerText[..sepIdx];
                string headerSecondary = headerText[(sepIdx + 5)..];
                DrawTextBold(headerPrimary, headerPos, LabelDim);
                float headerPrimaryWidth = MeasureText(headerPrimary + "  ").X;
                float headerRemaining = headerMaxWidth - headerPrimaryWidth;
                if (headerRemaining > 20)
                    DrawTextBold(TruncateText(headerSecondary, headerRemaining), new Vector2(headerPos.X + headerPrimaryWidth, headerY), LabelDim);
            }

            int lineY = headerY + itemHeight;
            byte lineAlpha = 60;
            float lineFactor = lineAlpha / 255f;
            var lineColor = new Color((byte)(TextColor.R * lineFactor), (byte)(TextColor.G * lineFactor), (byte)(TextColor.B * lineFactor), lineAlpha);
            Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), new Rectangle(Widget.Bounds.X + 4, lineY, Widget.Bounds.Width - 8, 1), lineColor);
            headerOffset = itemHeight + 2;
        }

        string[] items = Widget.ListBoxItems;
        if (items == null || items.Length == 0)
        {
            string emptyText = "No results";
            var emptySize = MeasureText(emptyText);
            var emptyPos = new Vector2(
                Widget.Bounds.X + (Widget.Bounds.Width - emptySize.X) / 2,
                Widget.Bounds.Y + headerOffset + (Widget.Bounds.Height - headerOffset - emptySize.Y) / 2);
            DrawText(emptyText, emptyPos, LabelDim);
            return;
        }

        int maxVisible = (Widget.Bounds.Height - 8 - headerOffset) / itemHeight;
        int scrollClamp = Math.Max(0, items.Length - maxVisible);
        int scrollOffset = Math.Clamp(Widget.ListBoxScroll, 0, scrollClamp);
        int count = Math.Min(items.Length - scrollOffset, maxVisible);
        var selected = Widget.ListBoxSelected;

        for (int i = 0; i < count; i++)
        {
            int itemIndex = i + scrollOffset;
            int itemY = Widget.Bounds.Y + 4 + headerOffset + i * itemHeight;
            string fullText = items[itemIndex];

            bool isSeparator = fullText.Length > 0 && fullText[0] == '\x03';
            if (isSeparator)
            {
                string headerText = fullText[1..];
                var headerSize = Renderer.MeasureString("Inter-Bold", FontSize, headerText);
                float textY = itemY + (itemHeight - headerSize.Y) / 2;
                float textX = Widget.Bounds.X + Padding;
                Renderer.DrawString("Inter-Bold", FontSize, headerText, new Vector2(textX, textY), LabelDim);
                int lineStartX = (int)(textX + headerSize.X + Padding);
                int lineEndX = Widget.Bounds.Right - Padding;
                if (lineStartX < lineEndX)
                {
                    int lineY = itemY + itemHeight / 2;
                    byte lineAlpha = 40;
                    float lineFactor = lineAlpha / 255f;
                    var lineColor = new Color((byte)(TextColor.R * lineFactor), (byte)(TextColor.G * lineFactor), (byte)(TextColor.B * lineFactor), lineAlpha);
                    Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), new Rectangle(lineStartX, lineY, lineEndX - lineStartX, 1), lineColor);
                }
                continue;
            }

            bool isSelected = selected != null && selected.Contains(itemIndex);

            if (isSelected)
            {
                var highlightRect = new Rectangle(Widget.Bounds.X + 4, itemY, Widget.Bounds.Width - 8, itemHeight);
                Renderer.DrawRoundedRect(highlightRect, SliderFill, 4);
            }

            bool isIndented = fullText.Length > 0 && (fullText[0] == '\x04' || fullText[0] == '\x05');
            int indent = isIndented ? 16 : 0;
            float maxWidth = Widget.Bounds.Width - Padding * 2 - indent;
            var itemPos = new Vector2(Widget.Bounds.X + Padding + indent, itemY);
            Color primaryColor = isSelected ? CloseText : TextColor;

            bool hasEye = fullText.Length > 0 && (fullText[0] == '\x06' || fullText[0] == '\x07');
            if (hasEye)
            {
                bool isEntityVisible = fullText[0] == '\x06';
                int eyeSize = 20;
                int eyeY = itemY + (itemHeight - eyeSize) / 2;
                var eyeTex = isEntityVisible ? Renderer.GetVisibilityTexture() : Renderer.GetVisibilityOffTexture();
                Color eyeColor = isEntityVisible ? ToggleOn : new Color(TextColor.R, TextColor.G, TextColor.B, (byte)60);
                Renderer.DrawSprite(eyeTex, new Rectangle((int)itemPos.X, eyeY, eyeSize, eyeSize), eyeColor);

                itemPos.X += eyeSize + 10;
                maxWidth -= eyeSize + 10;
                fullText = fullText[1..];
            }

            bool hasStatus = fullText.Length > 0 && (fullText[0] == '\x01' || fullText[0] == '\x02' || fullText[0] == '\x04' || fullText[0] == '\x05');
            if (hasStatus)
            {
                bool isItemEnabled = fullText[0] == '\x01' || fullText[0] == '\x04';
                bool isGrouped = fullText[0] == '\x04' || fullText[0] == '\x05';
                int controlSize = 20;
                int controlY = itemY + (itemHeight - controlSize) / 2;

                if (isGrouped)
                {
                    var circleTex = Renderer.GetCircleTexture(controlSize * 4);
                    Renderer.DrawSprite(circleTex, new Rectangle((int)itemPos.X, controlY, controlSize, controlSize), isItemEnabled ? ToggleOn : ToggleOff);
                    int hollowSize = controlSize - 4;
                    Color hollowColor = isSelected ? SliderFill : ButtonColor;
                    Renderer.DrawSprite(circleTex, new Rectangle((int)itemPos.X + 2, controlY + 2, hollowSize, hollowSize), hollowColor);
                    if (isItemEnabled)
                    {
                        int dotSize = controlSize - 8;
                        Renderer.DrawSprite(circleTex, new Rectangle((int)itemPos.X + 4, controlY + 4, dotSize, dotSize), SliderFill);
                    }
                }
                else
                {
                    var boxRect = new Rectangle((int)itemPos.X, controlY, controlSize, controlSize);
                    Renderer.DrawRoundedRect(boxRect, isItemEnabled ? ToggleOn : ToggleOff, 4);
                    if (isItemEnabled)
                    {
                        int checkSize = controlSize - 4;
                        var checkTex = Renderer.GetCheckmarkTexture(checkSize * 4);
                        Renderer.DrawSprite(checkTex, new Rectangle(boxRect.X + 2, boxRect.Y + 2, checkSize, checkSize), CloseText);
                    }
                }

                itemPos.X += controlSize + 10;
                maxWidth -= controlSize + 10;
                fullText = fullText[1..];
            }

            int separatorIdx = fullText.IndexOf("  |  ", StringComparison.Ordinal);
            if (separatorIdx < 0)
            {
                DrawText(TruncateText(fullText, maxWidth), itemPos, primaryColor);
            }
            else
            {
                string primary = fullText[..separatorIdx];
                string secondary = fullText[(separatorIdx + 5)..];
                DrawText(primary, itemPos, primaryColor);
                float primaryWidth = MeasureText(primary + "  ").X;
                float remainingWidth = maxWidth - primaryWidth;
                if (remainingWidth > 20)
                {
                    Color dimColor = isSelected ? new Color(CloseText.R, CloseText.G, CloseText.B, (byte)(CloseText.A * 0.6f)) : LabelDim;
                    DrawText(TruncateText(secondary, remainingWidth), new Vector2(itemPos.X + primaryWidth, itemY), dimColor);
                }
            }
        }

        if (items.Length > maxVisible)
        {
            int thumbWidth = 6;
            int thumbMargin = 4;
            int thumbX = Widget.Bounds.Right - thumbMargin - thumbWidth;
            int trackHeight = Widget.Bounds.Height - 8 - headerOffset;
            float thumbRatio = (float)maxVisible / items.Length;
            int thumbHeight = Math.Max(16, (int)(trackHeight * thumbRatio));
            int scrollRange = trackHeight - thumbHeight;
            int maxScroll = Math.Max(1, items.Length - maxVisible);
            int thumbY = Widget.Bounds.Y + 4 + headerOffset + (int)(scrollRange * ((float)scrollOffset / maxScroll));
            Renderer.DrawRoundedRect(new Rectangle(thumbX, thumbY, thumbWidth, thumbHeight), SliderFill, thumbWidth / 2);
        }
    }

    /// <summary> Draws a dropdown widget showing the selected option with a triangle indicator. </summary>
    private void DrawDropdown(Widget Widget, Vector2 Mouse, bool HoverBlocked, string WindowId)
    {
        bool hovered = !Widget.Disabled && !HoverBlocked && Widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y);
        Color bgColor = Widget.Disabled ? Dim(ButtonColor) : (hovered ? ButtonHover : ButtonColor);
        bool isOpen = OpenDropdownWindowId == WindowId && OpenDropdownWidgetId == Widget.Id;
        var borderRect = new Rectangle(Widget.Bounds.X - 1, Widget.Bounds.Y - 1, Widget.Bounds.Width + 2, Widget.Bounds.Height + 3);
        byte borderAlpha = Widget.Disabled ? (byte)25 : (byte)50;
        float borderFactor = borderAlpha / 255f;
        var borderColor = new Color((byte)(TextColor.R * borderFactor), (byte)(TextColor.G * borderFactor), (byte)(TextColor.B * borderFactor), borderAlpha);
        Renderer.DrawRoundedRect(borderRect, borderColor, CornerRadius + 1, isOpen ? RoundedCorners.Top : RoundedCorners.All);
        Renderer.DrawRoundedRect(Widget.Bounds, bgColor, CornerRadius, isOpen ? RoundedCorners.Top : RoundedCorners.All);

        string selectedLabel = Widget.DropdownOptions != null && Widget.DropdownSelected < Widget.DropdownOptions.Length
            ? Widget.DropdownOptions[Widget.DropdownSelected] : "?";
        string displayText = $"{Widget.Text}: {selectedLabel}";

        const int triSize = 8;
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

        var popupBorderRect = new Rectangle(OpenDropdownPopupBounds.X - 1, OpenDropdownPopupBounds.Y, OpenDropdownPopupBounds.Width + 2, OpenDropdownPopupBounds.Height + 1);
        byte popupBorderAlpha = 50;
        float popupBorderFactor = popupBorderAlpha / 255f;
        var popupBorderColor = new Color((byte)(TextColor.R * popupBorderFactor), (byte)(TextColor.G * popupBorderFactor), (byte)(TextColor.B * popupBorderFactor), popupBorderAlpha);
        Renderer.DrawRoundedRect(popupBorderRect, popupBorderColor, CornerRadius + 1, RoundedCorners.Bottom);
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
            int thumbWidth = 6;
            int thumbMargin = 4;
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
        float opacity = GlassTintOpacity;
        return new Color(
            (byte)(color.R * opacity),
            (byte)(color.G * opacity),
            (byte)(color.B * opacity),
            (byte)(color.A * opacity));
    }
}
