using CodoMetis.TypeKit.ClosedSets;
namespace AspectTestingSpike;
[global::System.Text.Json.Serialization.JsonConverterAttribute(typeof(global::AspectTestingSpike.OrderStatus.OrderStatusJsonConverter))]
[global::System.ComponentModel.TypeConverterAttribute(typeof(global::AspectTestingSpike.OrderStatus.OrderStatusTypeConverter))]
[global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSetAttribute<global::AspectTestingSpike.OrderStatus>]
[global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
public sealed partial class OrderStatus : IClosedSet<OrderStatus>, global::System.IParsable<global::AspectTestingSpike.OrderStatus>
{
  public static readonly OrderStatus Open = new();
  public static readonly OrderStatus Shipped = new();
  public static readonly OrderStatus Cancelled = new();
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  private static readonly global::System.Collections.Generic.IReadOnlyList<global::AspectTestingSpike.OrderStatus> _all = null !;
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  private global::System.String _name = null !;
  static OrderStatus()
  {
    global::AspectTestingSpike.OrderStatus.Open._name = "Open";
    global::AspectTestingSpike.OrderStatus.Shipped._name = "Shipped";
    global::AspectTestingSpike.OrderStatus.Cancelled._name = "Cancelled";
    global::AspectTestingSpike.OrderStatus._all = global::System.Array.AsReadOnly(new global::AspectTestingSpike.OrderStatus[] { global::AspectTestingSpike.OrderStatus.Open, global::AspectTestingSpike.OrderStatus.Shipped, global::AspectTestingSpike.OrderStatus.Cancelled });
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  private OrderStatus()
  {
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Collections.Generic.IReadOnlyList<global::AspectTestingSpike.OrderStatus> All
  {
    get
    {
      return (global::System.Collections.Generic.IReadOnlyList<global::AspectTestingSpike.OrderStatus>)(global::AspectTestingSpike.OrderStatus._all ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.String Name
  {
    get
    {
      return (global::System.String)(this._name ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NotAMember("OrderStatus"));
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.String QualifiedName
  {
    get
    {
      return (global::System.String)("OrderStatus." + this.Name);
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public TResult Match<TResult>(global::System.Func<TResult> open, global::System.Func<TResult> shipped, global::System.Func<TResult> cancelled)
  {
    global::System.ArgumentNullException.ThrowIfNull(open, "open");
    global::System.ArgumentNullException.ThrowIfNull(shipped, "shipped");
    global::System.ArgumentNullException.ThrowIfNull(cancelled, "cancelled");
    if (global::System.Object.ReferenceEquals(this, global::AspectTestingSpike.OrderStatus.Open))
      return (TResult)open.Invoke();
    if (global::System.Object.ReferenceEquals(this, global::AspectTestingSpike.OrderStatus.Shipped))
      return (TResult)shipped.Invoke();
    if (global::System.Object.ReferenceEquals(this, global::AspectTestingSpike.OrderStatus.Cancelled))
      return (TResult)cancelled.Invoke();
    throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NotAMember("OrderStatus");
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  static global::AspectTestingSpike.OrderStatus global::System.IParsable<global::AspectTestingSpike.OrderStatus>.Parse(global::System.String s, global::System.IFormatProvider? provider)
  {
    global::System.ArgumentNullException.ThrowIfNull(s);
    return (global::AspectTestingSpike.OrderStatus)global::AspectTestingSpike.OrderStatus.Parse(s);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.OrderStatus Parse(global::System.String? name)
  {
    if (name == "Open")
      return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    if (name == "Shipped")
      return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    if (name == "Cancelled")
      return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMember("OrderStatus");
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.OrderStatus Parse(global::System.String? name, global::System.Boolean ignoreCase)
  {
    var comparison = ignoreCase ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal;
    if (string.Equals(name, "Open", comparison))
      return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    if (string.Equals(name, "Shipped", comparison))
      return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    if (string.Equals(name, "Cancelled", comparison))
      return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
    throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMember("OrderStatus");
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public override global::System.String ToString()
  {
    return (global::System.String)this.Name;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  static global::System.Boolean global::System.IParsable<global::AspectTestingSpike.OrderStatus>.TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] global::System.String? s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] out global::AspectTestingSpike.OrderStatus result)
  {
    if (s == "Open")
    {
      result = global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    if (s == "Shipped")
    {
      result = global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    if (s == "Cancelled")
    {
      result = global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    result = null !;
    return (global::System.Boolean)false;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] global::System.String? name, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out global::AspectTestingSpike.OrderStatus? member)
  {
    if (name == "Open")
    {
      member = global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    if (name == "Shipped")
    {
      member = global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    if (name == "Cancelled")
    {
      member = global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    member = null;
    return (global::System.Boolean)false;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] global::System.String? name, global::System.Boolean ignoreCase, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out global::AspectTestingSpike.OrderStatus? member)
  {
    var comparison = ignoreCase ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal;
    if (string.Equals(name, "Open", comparison))
    {
      member = global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    if (string.Equals(name, "Shipped", comparison))
    {
      member = global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    if (string.Equals(name, "Cancelled", comparison))
    {
      member = global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus");
      return (global::System.Boolean)true;
    }
    member = null;
    return (global::System.Boolean)false;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public sealed class OrderStatusJsonConverter : global::System.Text.Json.Serialization.JsonConverter<global::AspectTestingSpike.OrderStatus>
  {
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::AspectTestingSpike.OrderStatus Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
      var name = reader.TokenType == global::System.Text.Json.JsonTokenType.String ? reader.GetString() : null;
      if (name is not null)
      {
        if (name == "Open")
          return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
        if (name == "Shipped")
          return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
        if (name == "Cancelled")
          return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
      }
      throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMemberInJson("OrderStatus");
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::AspectTestingSpike.OrderStatus ReadAsPropertyName(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
      var name = reader.GetString();
      if (name == "Open")
        return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Open ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
      if (name == "Shipped")
        return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Shipped ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
      if (name == "Cancelled")
        return (global::AspectTestingSpike.OrderStatus)(global::AspectTestingSpike.OrderStatus.Cancelled ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.StillInitializing("OrderStatus"));
      throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMemberInJson("OrderStatus");
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, global::AspectTestingSpike.OrderStatus value, global::System.Text.Json.JsonSerializerOptions options)
    {
      writer.WriteStringValue((string)value.Name);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override void WriteAsPropertyName(global::System.Text.Json.Utf8JsonWriter writer, global::AspectTestingSpike.OrderStatus value, global::System.Text.Json.JsonSerializerOptions options)
    {
      writer.WritePropertyName((string)value.Name);
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public sealed class OrderStatusTypeConverter : global::System.ComponentModel.TypeConverter
  {
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::System.Boolean CanConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type sourceType)
    {
      return (global::System.Boolean)(sourceType == typeof(global::System.String) || base.CanConvertFrom(context, sourceType));
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::System.Boolean CanConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type? destinationType)
    {
      return (global::System.Boolean)(destinationType == typeof(global::System.String) || base.CanConvertTo(context, destinationType));
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::System.Object? ConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, global::System.Object value)
    {
      if (value is string s)
      {
        return global::AspectTestingSpike.OrderStatus.Parse(s);
      }
      return (global::System.Object? )base.ConvertFrom(context, culture, value);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::System.Object? ConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, global::System.Object? value, global::System.Type destinationType)
    {
      if (destinationType == typeof(global::System.String) && value is not null)
      {
        return ((global::AspectTestingSpike.OrderStatus)value).Name;
      }
      return (global::System.Object? )base.ConvertTo(context, culture, value, destinationType);
    }
  }
}