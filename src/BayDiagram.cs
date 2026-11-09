// SPDX-FileCopyrightText: 2026 XuFly
// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModuleAdvisor
{
    internal static class BayDiagram
    {
        private sealed class Entry
        {
            internal GameItem Item;
            internal GridShape Shape;
            internal string Id, Tip;
            internal Color Color;
        }

        internal static void Draw(CustomUIBuilder builder, CustomUIWindow window, Bay bay, IReadOnlyList<Placement> additions,
            IReadOnlyList<GameItem> modules, BayPrediction prediction)
        {
            int width = bay.Grid.inventoryShape.width;
            int height = bay.Grid.inventoryShape.height;
            if (width > 24 || height > 24)
            {
                builder.AddLabel(L.Text("diagram_large"));
                foreach (var p in additions)
                    builder.AddLabel(L.Text("placement", GameAdapter.Name(modules[p.Module]), p.X + 1, p.Y + 1,
                        p.Rotation * 90, p.Flipped ? L.Text("yes") : L.Text("no")));
                return;
            }
            var entries = new List<Entry>();
            foreach (var item in bay.Grid.childItems)
                entries.Add(new Entry {Item = item, Shape = item.modifiedShape, Id = "E" + (entries.Count + 1),
                    Tip = GameAdapter.Details(item), Color = new Color(.25f, .35f, .48f)});
            for (int i = 0; i < additions.Count; i++)
            {
                var p = additions[i];
                var item = modules[p.Module];
                var stats = prediction.ItemStats[prediction.ExistingCount + i];
                entries.Add(new Entry {Item = item,
                    Shape = new GridShapeBuilder(item.modifiedShape).SetTransform(p.X, p.Y, p.Flipped, p.Rotation),
                    Id = "N" + (i + 1), Color = Color.HSVToRGB(((i + 1) * .173f) % 1f, .45f, .5f),
                    Tip = GameAdapter.Details(item) + "\n" + L.Text("placement", GameAdapter.Name(item), p.X + 1, p.Y + 1,
                        p.Rotation * 90, p.Flipped ? L.Text("yes") : L.Text("no")) + "\n"
                        + L.Text("estimate_layout", stats[0], stats[1], stats[2])});
            }
            float cell = Mathf.Min(40, 600f / (width + 1));
            builder.AddLabel(L.Text("diagram_legend"));
            builder.BeginPanel((width + 1) * cell, (height + 1) * cell, TextAnchor.UpperLeft, 0, 0);
            var frame = builder.CurrentContainer;
            var canvas = ItemVisual.Rect(frame, "BayCanvas", Vector2.zero, new Vector2((width + 1) * cell, (height + 1) * cell));
            canvas.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var cells = new CustomUIBuilder(CustomUIManager.Instance, window, canvas);
            cells.BeginGrid(width + 1, cell, cell, 0);
            var grid = cells.CurrentContainer;
            grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0, 1);
            grid.anchoredPosition = Vector2.zero;
            grid.sizeDelta = canvas.sizeDelta;
            for (int y = -1; y < height; y++)
                for (int x = -1; x < width; x++)
                {
                    bool axis = x < 0 || y < 0;
                    var entry = axis ? null : entries.FirstOrDefault(e => e.Shape.Get(x, y) == 1);
                    bool blocked = !axis && bay.Grid.inventoryShape.Get(x, y) != 0;
                    string text = axis ? (x < 0 ? (y < 0 ? "" : (y + 1).ToString()) : (x + 1).ToString())
                        : entry != null ? "" : blocked ? "×" : "·";
                    cells.AddButton(text, () => { }).WithTooltip(entry?.Tip ?? (axis ? "" : L.Text(blocked ? "diagram_blocked" : "diagram_free")));
                    var element = cells.Last;
                    if (element.button != null) element.button.transition = Selectable.Transition.None;
                    if (element.image != null) element.image.color = entry?.Color ?? (blocked
                        ? new Color(.12f, .12f, .15f) : new Color(.22f, .24f, .27f));
                    if (element.text != null) {element.text.fontSize = 12; element.text.alignment = TextAlignmentOptions.Center;}
                }
            cells.End();
            foreach (var e in entries) ItemVisual.Overlay(canvas, e.Item, e.Shape, cell, cell);
            // Badges are drawn last so sprites cannot cover the identifiers.
            foreach (var e in entries)
            {
                var occupied = GameAdapter.Cells(e.Shape, width);
                if (occupied.Length == 0) continue;
                int first = occupied.Min();
                var badge = ItemVisual.Rect(canvas, e.Id, new Vector2(cell + (first % width) * cell + 1,
                    -cell - (first / width) * cell - 1), new Vector2(Mathf.Min(28, cell - 2), Mathf.Min(16, cell - 2)));
                badge.gameObject.AddComponent<Image>().color = new Color(.04f, .04f, .06f, .9f);
                badge.GetComponent<Image>().raycastTarget = false;
                var label = ItemVisual.Rect(badge, "Id", Vector2.zero, badge.sizeDelta).gameObject.AddComponent<TextMeshProUGUI>();
                label.font = cells.Last.text != null ? cells.Last.text.font : null;
                label.text = e.Id;
                label.fontSize = Mathf.Min(11, cell * .35f);
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }
            builder.End();
            foreach (var e in entries)
            {
                builder.BeginRow(6, TextAnchor.MiddleLeft);
                ItemVisual.Icon(builder, e.Item, e.Tip, 30);
                builder.AddLabel(e.Id + " · " + e.Item.GetDisplayName(true)).WithTooltip(e.Tip);
                builder.End();
            }
        }
    }
}
