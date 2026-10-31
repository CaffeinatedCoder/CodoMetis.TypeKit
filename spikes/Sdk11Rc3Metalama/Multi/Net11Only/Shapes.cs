public sealed record Circle(double Radius);

public sealed record Square(double Side);

public union Shape(Circle, Square);

public static class Shapes
{
    public static string Describe(Shape shape) => shape switch
    {
        Circle c => $"circle {c.Radius}",
        Square s => $"square {s.Side}",
    };
}
