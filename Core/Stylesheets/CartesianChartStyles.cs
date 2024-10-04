using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace Core.Stylesheets;

//pulled straight from the examples: https://livecharts.dev/docs/Avalonia/2.0.0-rc2/samples.axes.style
public static class CartesianChartStyles
{
    static class ChartColors
    {
        public static readonly SKColor SkGray = new(195, 195, 195);
        public static readonly SKColor SkGray1 = new(160, 160, 160);
        public static readonly SKColor SkGray2 = new(90, 90, 90);
        public static readonly SKColor SkGray3 = new(60, 60, 60);
    }
    
    public static int GeometrySize { get; set; } = 1;
    public static SolidColorPaint StrokeColor = new SolidColorPaint(SKColors.Red, 1);
    
    public static DrawMarginFrame MarginFrame { get; set; } =
        new()
        {
            Stroke = new SolidColorPaint
            {
                Color = ChartColors.SkGray,
                StrokeThickness = 2
            }
        };
    
    public static class AxesStyles
    {
        public static Axis[] XAxis { get; set; } =
        {
            new Axis
            {
                NamePaint = new SolidColorPaint(ChartColors.SkGray1),
                TextSize = 18,
                Padding = new Padding(5, 15, 5, 5),
                LabelsPaint = new SolidColorPaint(ChartColors.SkGray),
                SeparatorsPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray,
                    StrokeThickness = 1,
                    PathEffect = new DashEffect(new float[] { 3, 3 })
                },
                SubseparatorsPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray2,
                    StrokeThickness = 0.5f
                },
                SubseparatorsCount = 9,
                ZeroPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray1,
                    StrokeThickness = 2
                },
                TicksPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray,
                    StrokeThickness = 1.5f
                },
                SubticksPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray,
                    StrokeThickness = 1
                }
            }
        };

        public static Axis[] YAxis { get; set; } =
        {
            new Axis
            {
                NamePaint = new SolidColorPaint(ChartColors.SkGray1),
                TextSize = 18,
                Padding = new Padding(5, 15, 5, 5),
                LabelsPaint = new SolidColorPaint(ChartColors.SkGray),
                SeparatorsPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray,
                    StrokeThickness = 1,
                    PathEffect = new DashEffect(new float[] { 3, 3 })
                },
                SubseparatorsPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray2,
                    StrokeThickness = 0.5f
                },
                SubseparatorsCount = 9,
                ZeroPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray1,
                    StrokeThickness = 2
                },
                TicksPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray,
                    StrokeThickness = 1.5f
                },
                SubticksPaint = new SolidColorPaint
                {
                    Color = ChartColors.SkGray,
                    StrokeThickness = 1
                }
            }
        };
    }
}