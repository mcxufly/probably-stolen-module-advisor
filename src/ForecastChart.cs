using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ModuleAdvisor
{
    internal static class ForecastChart
    {
        internal static void Draw(CustomUIBuilder builder, double[] chosen, double[] instant)
        {
            float width = 560, height = 130;
            builder.BeginPanel(width + 20, height + 20);
            var host = builder.CurrentContainer;
            double min = Math.Min(0, Math.Min(chosen.Min(), instant.Min()));
            double max = Math.Max(0, Math.Max(chosen.Max(), instant.Max()));
            if (max - min < 1e-9) max = min + 1;
            Func<int, double, Vector2> point = (i, value) => new Vector2(
                width * i / Math.Max(1, chosen.Length - 1), (float)(height * (value - min) / (max - min)));
            Action<Vector2, Vector2, Color> segment = (a, b, color) => {
                var go = new GameObject("ForecastLine", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.transform.SetParent(host, false);
                go.GetComponent<LayoutElement>().ignoreLayout = true;
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0, .5f);
                rect.anchoredPosition = a + new Vector2(10, 10);
                rect.sizeDelta = new Vector2((b - a).magnitude, 2);
                rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                var image = go.GetComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
            };
            segment(point(0, 0), point(chosen.Length - 1, 0), new Color(.5f, .5f, .5f));
            var blue = new Color(.35f, .75f, 1);
            var orange = new Color(1, .65f, .25f);
            if (chosen.Length == 1)
            {
                var a = point(0, chosen[0]);
                var b = point(0, instant[0]);
                segment(a, a + new Vector2(8, 0), blue);
                segment(b, b + new Vector2(8, 0), orange);
            }
            for (int i = 1; i < chosen.Length; i++)
            {
                segment(point(i - 1, chosen[i - 1]), point(i, chosen[i]), blue);
                segment(point(i - 1, instant[i - 1]), point(i, instant[i]), orange);
            }
            builder.End();
            builder.AddLabel(L.Text("forecast_chart", min.ToString("0.##"), max.ToString("0.##"), chosen.Length));
        }
    }
}
