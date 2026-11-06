using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModuleAdvisor
{
    // Original UI-only grid: no sprites, textures, or game inventory changes.
    internal static class BayDiagram
    {
        internal static void Draw(CustomUIBuilder builder, CustomUIWindow window, Bay bay, IReadOnlyList<Placement> additions,
            IReadOnlyList<GameItem> modules)
        {
            int width = bay.Grid.inventoryShape.width;
            int height = bay.Grid.inventoryShape.height;
            if (width > 24 || height > 24)
            {
                builder.AddLabel(L.Text("diagram_large"));
                return;
            }
            var cells = new string[width * height];
            var colors = new Color[cells.Length];
            var tips = new string[cells.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int cell = y * width + x;
                    bool blocked = bay.Grid.inventoryShape.Get(x, y) != 0;
                    cells[cell] = blocked ? "X" : "·";
                    colors[cell] = blocked ? new Color(.18f, .18f, .2f) : new Color(.3f, .32f, .34f);
                    tips[cell] = L.Text(blocked ? "diagram_blocked" : "diagram_free");
                }
            var legend = new List<string>();
            int number = 0;
            foreach (var item in bay.Grid.childItems)
            {
                string label = "E" + ++number;
                legend.Add(label + " — " + GameAdapter.Name(item));
                foreach (int cell in GameAdapter.Cells(item.modifiedShape, width))
                    if (cell >= 0 && cell < cells.Length)
                    {
                        cells[cell] = label;
                        colors[cell] = new Color(.32f, .4f, .52f);
                        tips[cell] = GameAdapter.Details(item);
                    }
            }
            number = 0;
            foreach (var placement in additions)
            {
                var module = modules[placement.Module];
                string label = "N" + ++number;
                legend.Add(label + " — " + GameAdapter.Name(module));
                Color color = Color.HSVToRGB((number * .173f) % 1f, .55f, .65f);
                foreach (int cell in placement.Cells)
                {
                    cells[cell] = label;
                    colors[cell] = color;
                    tips[cell] = GameAdapter.Details(module);
                }
            }
            builder.AddLabel(L.Text("diagram_legend"));
            builder.BeginGrid(width + 1, 34, 30, 2);
            int serial = 0;
            Action<string, Color, string> cellUi = (text, color, tip) =>
            {
                string id = "diagram_" + bay.Machine.uniqueId + "_" + bay.Number + "_" + serial++;
                builder.AddButton(text, () => { }, id).WithTooltip(tip);
                var element = window.Get(id);
                element.SetPreferredSize(34, 30);
                if (element.button != null) element.button.transition = Selectable.Transition.None;
                if (element.image != null) element.image.color = color;
                if (element.text != null)
                {
                    element.text.alignment = TextAlignmentOptions.Center;
                    element.text.fontSize = 12;
                    element.text.enableAutoSizing = false;
                    element.text.color = Color.white;
                }
            };
            var axis = new Color(.22f, .24f, .27f);
            cellUi("", axis, "");
            for (int x = 0; x < width; x++) cellUi((x + 1).ToString(), axis, L.Text("diagram_column"));
            for (int y = 0; y < height; y++)
            {
                cellUi((y + 1).ToString(), axis, L.Text("diagram_row"));
                for (int x = 0; x < width; x++)
                {
                    int cell = y * width + x;
                    cellUi(cells[cell], colors[cell], tips[cell]);
                }
            }
            builder.End();
            foreach (string line in legend) builder.AddLabel(line);
        }
    }
}
