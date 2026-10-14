using CodoMetis.TypeKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

// In EF Core's namespace, as the providers' UseNpgsql and UseSqlite are: the file that configures a
// DbContext imports it already, so UseTypeKit() needs no using of its own.
namespace Microsoft.EntityFrameworkCore;

/// <summary>Adds CodoMetis.TypeKit to a <see cref="DbContext"/>.</summary>
public static class TypeKitDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Maps every value object in the model to a column of the type it wraps, and translates
    /// <c>.Value</c>, <c>GetValue()</c> and <c>ValueOrNull()</c> in queries into that column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Value objects and closed sets are recognised wherever EF meets them, by the attribute the
    /// generators put on each (<see cref="CodoMetis.TypeKit.CompilerServices.GeneratedValueObjectAttribute{TValueObject,T}"/>,
    /// whose type arguments are constrained to <see cref="CodoMetis.TypeKit.ValueObjects.IValueObject{TSelf,T}"/>,
    /// and <see cref="CodoMetis.TypeKit.CompilerServices.GeneratedClosedSetAttribute{TSet}"/>): properties,
    /// keys, foreign keys, elements of primitive collections and query parameters. Never by name, and
    /// never by listing a type's interfaces, which trimming can empty. Nothing is registered per type.
    /// Reading a column back does not apply the value object's rules
    /// (<see cref="CodoMetis.TypeKit.ValueObjects.IValueObjectMaterializer{TSelf,T}"/>).
    /// </para>
    /// <para>
    /// It only adds plugins, conventions and an interceptor, so it replaces nothing of EF's or of
    /// another library's, and the order against the database provider does not matter.
    /// </para>
    /// </remarks>
    /// <param name="optionsBuilder">The options being configured.</param>
    /// <returns>The same builder.</returns>
    public static DbContextOptionsBuilder UseTypeKit(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<TypeKitOptionsExtension>() ?? new TypeKitOptionsExtension());

        return optionsBuilder;
    }

    /// <inheritdoc cref="UseTypeKit(DbContextOptionsBuilder)"/>
    /// <typeparam name="TContext">The context type.</typeparam>
    public static DbContextOptionsBuilder<TContext> UseTypeKit<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTypeKit((DbContextOptionsBuilder)optionsBuilder);
}
