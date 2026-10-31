namespace UnionLib;

public sealed record Cat(string Name);

public sealed record Dog(string Name);

public union Pet(Cat, Dog);

public static class Pets
{
    public static Pet Adopt() => new Cat("Tom");
}
