// SPDX-FileCopyrightText: 2026 XuFly
// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;
using UnityEngine.UI;

namespace ModuleAdvisor
{
    internal static class ItemVisual
    {
        // Read the live background, not the root image or a guessed atlas name. This also
        // supports mod sprites, state changes and the currently displayed animation frame.
        internal static Sprite Sprite(GameItem item) => (item as GameItemElement)?.background?.image?.sprite;

        internal static void Icon(CustomUIBuilder builder, GameItem item, string tooltip, float size = 44)
        {
            var sprite = Sprite(item);
            builder.AddImage(sprite, size, size).WithTooltip(tooltip);
            var element = builder.Last;
            element.SetFixedWidth(size).SetPreferredSize(size, size);
            if (element.image != null)
            {
                element.image.sprite = sprite;
                element.image.enabled = sprite != null;
                element.image.preserveAspect = true;
                element.image.color = Color.white;
            }
        }

        internal static void Overlay(RectTransform host, GameItem item, GridShape shape, float cellSize, float offset)
        {
            if (!(item is GameItemElement source) || source.background?.image?.sprite == null) return;
            float grid = UISettings.current.gridSize;
            if (grid <= 0) return;
            var root = Rect(host, "ItemSprite", new Vector2(offset + shape.minX * cellSize, -offset - shape.minY * cellSize),
                new Vector2(shape.width * grid, shape.height * grid));
            root.localScale = Vector3.one * (cellSize / grid);
            var rotator = Rect(root, "Rotation", Vector2.zero, root.sizeDelta);
            float flip = shape.flipped ? 1 : 0;
            rotator.pivot = shape.orientation == 0 ? new Vector2(flip, 1)
                : shape.orientation == 1 ? new Vector2(1 - flip, 1)
                : shape.orientation == 2 ? new Vector2(1 - flip, 0) : new Vector2(flip, 0);
            rotator.localScale = new Vector3(shape.flipped ? -1 : 1, 1, 1);
            rotator.localRotation = Quaternion.Euler(0, 0, 90 * shape.orientation);
            CopyImage(source.background.image, rotator, true);
            // Innate overlays are explicitly exposed by the game. Never clone inventory
            // scripts or child item trees, nor reparent any real game render nodes.
            foreach (var layer in source.GetActiveInnateSpriteMods())
                if (layer?.image != null) CopyImage(layer.image, rotator, true, true);
        }

        private static void CopyImage(Image source, RectTransform parent, bool force = false, bool preserveTint = false)
        {
            if (source.sprite == null || (!force && (!source.enabled || !source.gameObject.activeSelf))) return;
            var go = new GameObject("SpriteLayer", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var original = source.rectTransform;
            rect.anchorMin = original.anchorMin;
            rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot;
            rect.sizeDelta = original.sizeDelta;
            rect.anchoredPosition = original.anchoredPosition;
            rect.localScale = original.localScale;
            rect.localRotation = original.localRotation;
            var image = go.GetComponent<Image>();
            image.sprite = source.sprite;
            image.color = preserveTint ? source.color : Color.white; // Innate tint may carry semantic color.
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
        }

        internal static RectTransform Rect(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
