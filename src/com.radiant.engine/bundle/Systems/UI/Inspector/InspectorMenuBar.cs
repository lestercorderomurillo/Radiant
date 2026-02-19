using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private const int MenuBarHeight = 36;
    private const int MenuBarPaddingX = 20;
    private const int MenuItemPaddingX = 24;
    private const int MenuItemHeight = 40;
    private const int MenuDropdownWidth = 320;
    private const float MenuBarFontSize = 22f;


    private List<MenuData> Menus = new();
    private string OpenMenuId;
    private Rectangle MenuBarBounds;
    private Rectangle OpenMenuDropdownBounds;

    private void InitializeMenuBar()
    {
        Menus.Clear();

        var about = new MenuData { Id = "about", Label = "Help" };
        about.Items.Add(new MenuItem
        {
            Id = "about_radiant",
            Label = "About Radiant",
            Type = MenuItemType.Action,
            ActionCallback = () =>
            {
                if (!Windows.ContainsKey("about_radiant"))
                {
                    CreateWindow("about_radiant", "About Radiant", 999, AutoPosition: false);
                    AddSectionLabel("about_radiant", "about_engine", "Radiant Engine");
                    AddLabel("about_radiant", "about_desc", "A lightweight 2D engine.");
                    AddLabel("about_radiant", "about_spacer1", " ");
                    AddLabel("about_radiant", "about_f2", "High-performance entity system.");
                    AddLabel("about_radiant", "about_f3", "GPU-accelerated rendering.");
                    AddLabel("about_radiant", "about_f1", "Built-in editor, systems and tools.");
                    AddLabel("about_radiant", "about_spacer2", " ");
                    AddLabel("about_radiant", "about_author", "Developed by Lester Cordero Murillo");
                    AddLabel("about_radiant", "about_build", ".NET 8.0 | Build 2026.02");
                }
                if (Windows.TryGetValue("about_radiant", out var aboutWindow))
                {
                    int windowHeight = ComputeWindowHeight(aboutWindow);
                    aboutWindow.Position = new Vector2(
                        (Renderer.VirtualWidth / UIScale - aboutWindow.Size.X) / 2,
                        (Renderer.VirtualHeight / UIScale - windowHeight) / 2);
                }
                ShowWindow("about_radiant");
            }
        });
        var workspace = new MenuData { Id = "workspace", Label = "Workspaces" };
        Menus.Add(workspace);

        Menus.Add(about);

        RebuildWorkspaceMenu();
        ComputeMenuBarLayout();
    }

    private void RebuildWorkspaceMenu()
    {
        MenuData workspace = null;
        foreach (var menu in Menus)
        {
            if (menu.Id == "workspace") { workspace = menu; break; }
        }
        if (workspace == null) return;

        workspace.Items.Clear();

        var ordered = new List<WindowData>(Windows.Values);
        ordered.Sort((a, b) =>
        {
            int order = a.LayoutOrder.CompareTo(b.LayoutOrder);
            return order != 0 ? order : a.CreationIndex.CompareTo(b.CreationIndex);
        });

        foreach (var window in ordered)
        {
            if (window.Id == "about_radiant") continue;
            string windowId = window.Id;
            workspace.Items.Add(new MenuItem
            {
                Id = "show_" + windowId,
                Label = window.Title,
                Type = MenuItemType.Toggle,
                ToggleValue = window.Visible,
                ToggleCallback = (value) => SetWindowVisible(windowId, value)
            });
        }

        workspace.Items.Add(new MenuItem
        {
            Id = "reorder_windows",
            Label = "Reset Positions",
            Type = MenuItemType.Action,
            ActionCallback = () =>
            {
                LayoutDone = false;
                WindowsRestored?.Invoke();
            }
        });
    }

    private void SyncWorkspaceToggleValues()
    {
        MenuData workspace = null;
        foreach (var menu in Menus)
        {
            if (menu.Id == "workspace") { workspace = menu; break; }
        }
        if (workspace == null) return;

        for (int i = 0; i < workspace.Items.Count; i++)
        {
            var item = workspace.Items[i];
            if (item.Type != MenuItemType.Toggle) continue;

            string windowId = item.Id.Length > 5 ? item.Id[5..] : "";
            if (Windows.TryGetValue(windowId, out var window))
            {
                item.ToggleValue = window.Visible;
                workspace.Items[i] = item;
            }
        }
    }

    private void ComputeMenuBarLayout()
    {
        float virtualWidth = Renderer?.VirtualWidth ?? 3840;
        MenuBarBounds = new Rectangle(0, 0, (int)(virtualWidth / UIScale), MenuBarHeight);

        float currentX = MenuBarPaddingX;
        foreach (var menu in Menus)
        {
            var labelSize = Renderer?.MeasureString("Inter", MenuBarFontSize, menu.Label) ?? new Vector2(80, 22);
            int headerWidth = (int)labelSize.X + MenuItemPaddingX * 2;
            menu.HeaderBounds = new Rectangle((int)currentX, 0, headerWidth, MenuBarHeight);
            currentX += headerWidth;
        }
    }

    private bool UpdateMenuBar(Vector2 VirtualMouse, bool LeftPressed, KeyboardState Keyboard)
    {
        ComputeMenuBarLayout();

        if (OpenMenuId != null && Keyboard.IsKeyDown(Keys.Escape) && PrevKeyState.IsKeyUp(Keys.Escape))
        {
            CloseMenuDropdown();
            return true;
        }

        bool overBar = MenuBarBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);
        bool overDropdown = OpenMenuId != null && OpenMenuDropdownBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);

        if (overBar || overDropdown)
            MouseOverUI = true;

        if (overBar && OpenMenuId != null)
        {
            foreach (var menu in Menus)
            {
                if (menu.HeaderBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y) && menu.Id != OpenMenuId)
                {
                    OpenMenuDropdown(menu.Id);
                    return true;
                }
            }
        }

        if (LeftPressed)
        {
            if (overBar)
            {
                foreach (var menu in Menus)
                {
                    if (menu.HeaderBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
                    {
                        if (OpenMenuId == menu.Id)
                            CloseMenuDropdown();
                        else
                            OpenMenuDropdown(menu.Id);
                        return true;
                    }
                }
                return true;
            }

            if (overDropdown)
            {
                HandleMenuItemClick(VirtualMouse);
                return true;
            }

            if (OpenMenuId != null)
            {
                CloseMenuDropdown();
                return true;
            }
        }

        return overBar || overDropdown;
    }

    private void OpenMenuDropdown(string MenuId)
    {
        OpenMenuId = MenuId;

        if (MenuId == "workspace")
        {
            RebuildWorkspaceMenu();
            SyncWorkspaceToggleValues();
        }

        MenuData menu = null;
        foreach (var m in Menus)
        {
            if (m.Id == MenuId) { menu = m; break; }
        }
        if (menu == null) return;

        int dropdownHeight = menu.Items.Count * MenuItemHeight;
        OpenMenuDropdownBounds = new Rectangle(menu.HeaderBounds.X, MenuBarHeight, MenuDropdownWidth, dropdownHeight);
    }

    private void CloseMenuDropdown()
    {
        OpenMenuId = null;
    }

    private void HandleMenuItemClick(Vector2 Mouse)
    {
        MenuData menu = null;
        foreach (var m in Menus)
        {
            if (m.Id == OpenMenuId) { menu = m; break; }
        }
        if (menu == null) return;

        int localIndex = ((int)Mouse.Y - OpenMenuDropdownBounds.Y) / MenuItemHeight;
        if (localIndex < 0 || localIndex >= menu.Items.Count) return;

        var item = menu.Items[localIndex];
        if (item.Type == MenuItemType.Toggle)
        {
            item.ToggleValue = !item.ToggleValue;
            menu.Items[localIndex] = item;
            item.ToggleCallback?.Invoke(item.ToggleValue);
        }
        else if (item.Type == MenuItemType.Action && item.ActionCallback != null)
        {
            item.ActionCallback.Invoke();
            CloseMenuDropdown();
        }
    }

    private void DrawMenuBar(Vector2 VirtualMouse)
    {
        var solid = Renderer.GetSolidTexture(Color.White);
        Color barBg = BlurResult != null ? GlassTint(TitleBarColor) : TitleBarColor;
        Renderer.DrawSprite(solid, MenuBarBounds, barBg);

        foreach (var menu in Menus)
        {
            bool isOpen = OpenMenuId == menu.Id;
            bool hovered = menu.HeaderBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);

            if (isOpen || hovered)
            {
                Color highlightBg = BlurResult != null ? GlassTint(ButtonHover) : ButtonHover;
                Renderer.DrawSprite(solid, menu.HeaderBounds, highlightBg);
            }

            var labelSize = Renderer.MeasureString("Inter", MenuBarFontSize, menu.Label);
            var labelPos = new Vector2(
                menu.HeaderBounds.X + (menu.HeaderBounds.Width - labelSize.X) / 2,
                menu.HeaderBounds.Y + (menu.HeaderBounds.Height - labelSize.Y) / 2);
            Renderer.DrawString("Inter", MenuBarFontSize, menu.Label, labelPos, TextColor);
        }
    }

    private const float MenuBarSmallFontSize = 18f;

    private (string Text, float FontSize) FitMenuItemText(string label, float maxWidth)
    {
        var size = Renderer.MeasureString("Inter", MenuBarFontSize, label);
        if (size.X <= maxWidth) return (label, MenuBarFontSize);

        size = Renderer.MeasureString("Inter", MenuBarSmallFontSize, label);
        if (size.X <= maxWidth) return (label, MenuBarSmallFontSize);

        var ellipsis = "...";
        for (int length = label.Length - 1; length > 0; length--)
        {
            var truncated = label[..length] + ellipsis;
            size = Renderer.MeasureString("Inter", MenuBarSmallFontSize, truncated);
            if (size.X <= maxWidth) return (truncated, MenuBarSmallFontSize);
        }
        return (ellipsis, MenuBarSmallFontSize);
    }

    private void DrawMenuDropdown(Vector2 VirtualMouse)
    {
        if (OpenMenuId == null) return;

        MenuData menu = null;
        foreach (var m in Menus)
        {
            if (m.Id == OpenMenuId) { menu = m; break; }
        }
        if (menu == null) return;

        bool glass = BlurResult != null;
        Color dropdownBg = glass ? GlassTint(WindowBg) : WindowBg;
        Renderer.DrawRoundedRect(OpenMenuDropdownBounds, dropdownBg, CornerRadius, RoundedCorners.Bottom);

        var solid = Renderer.GetSolidTexture(Color.White);
        for (int i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            var itemRect = new Rectangle(OpenMenuDropdownBounds.X, OpenMenuDropdownBounds.Y + i * MenuItemHeight, OpenMenuDropdownBounds.Width, MenuItemHeight);
            bool hovered = itemRect.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);

            if (hovered)
            {
                RoundedCorners corners = menu.Items.Count == 1 ? RoundedCorners.Bottom :
                    i == menu.Items.Count - 1 ? RoundedCorners.Bottom : RoundedCorners.None;
                Renderer.DrawRoundedRect(itemRect, glass ? GlassTint(ButtonHover) : ButtonHover, CornerRadius, corners);
            }

            if (item.Type == MenuItemType.Toggle)
            {
                int boxY = itemRect.Y + (itemRect.Height - ToggleBoxSize) / 2;
                var boxRect = new Rectangle(itemRect.X + Padding, boxY, ToggleBoxSize, ToggleBoxSize);
                Renderer.DrawRoundedRect(boxRect, item.ToggleValue ? ToggleOn : ToggleOff, 4);

                if (item.ToggleValue)
                {
                    int checkSize = ToggleBoxSize - 4;
                    var checkTex = Renderer.GetCheckmarkTexture(checkSize * 4);
                    var checkRect = new Rectangle(boxRect.X + 2, boxRect.Y + 2, checkSize, checkSize);
                    Renderer.DrawSprite(checkTex, checkRect, CloseText);
                }

                float textStartX = itemRect.X + Padding + ToggleBoxSize + 8;
                float maxTextWidth = itemRect.Right - textStartX - Padding;
                var (fitText, fitSize) = FitMenuItemText(item.Label, maxTextWidth);
                float fitLineHeight = Renderer.MeasureString("Inter", fitSize, fitText).Y;
                var textPos = new Vector2(textStartX, itemRect.Y + (itemRect.Height - fitLineHeight) / 2);
                Renderer.DrawString("Inter", fitSize, fitText, textPos, TextColor);
            }
            else
            {
                float textStartX = itemRect.X + Padding;
                float maxTextWidth = itemRect.Right - textStartX - Padding;
                var (fitText, fitSize) = FitMenuItemText(item.Label, maxTextWidth);
                float fitLineHeight = Renderer.MeasureString("Inter", fitSize, fitText).Y;
                var textPos = new Vector2(textStartX, itemRect.Y + (itemRect.Height - fitLineHeight) / 2);
                Renderer.DrawString("Inter", fitSize, fitText, textPos, TextColor);
            }
        }
    }
}
