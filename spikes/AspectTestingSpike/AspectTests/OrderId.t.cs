using CodoMetis.TypeKit.ValueObjects;
namespace AspectTestingSpike;
[global::CodoMetis.TypeKit.CompilerServices.GeneratedValueObjectAttribute<global::AspectTestingSpike.OrderId, global::System.Guid>]
[global::System.Text.Json.Serialization.JsonConverterAttribute(typeof(global::AspectTestingSpike.OrderId.OrderIdJsonConverter))]
[global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
[global::System.ComponentModel.TypeConverterAttribute(typeof(global::AspectTestingSpike.OrderId.OrderIdTypeConverter))]
public readonly partial record struct OrderId : IValue<Guid>, global::CodoMetis.TypeKit.ValueObjects.IValueObjectMaterializer<global::AspectTestingSpike.OrderId, global::System.Guid>, global::CodoMetis.TypeKit.ValueObjects.IValueObject<global::AspectTestingSpike.OrderId, global::System.Guid>, global::CodoMetis.TypeKit.ValueObjects.IPlainValueObject<global::AspectTestingSpike.OrderId, global::System.Guid>, global::System.Numerics.IEqualityOperators<global::AspectTestingSpike.OrderId, global::AspectTestingSpike.OrderId, global::System.Boolean>, global::System.IParsable<global::AspectTestingSpike.OrderId>, global::System.ISpanParsable<global::AspectTestingSpike.OrderId>, global::System.IUtf8SpanParsable<global::AspectTestingSpike.OrderId>, global::System.IFormattable, global::System.ISpanFormattable, global::System.IUtf8SpanFormattable, global::System.IComparable<global::AspectTestingSpike.OrderId>, global::System.IComparable, global::System.Numerics.IComparisonOperators<global::AspectTestingSpike.OrderId, global::AspectTestingSpike.OrderId, global::System.Boolean>
{
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  private readonly global::System.Guid _value;
  [global::System.Diagnostics.DebuggerHiddenAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  public OrderId()
  {
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private OrderId(global::System.Guid value)
  {
    this._value = value;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Guid Value
  {
    get
    {
      return (global::System.Guid)this._value;
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Int32 CompareTo(global::AspectTestingSpike.OrderId other)
  {
    return (global::System.Int32)(int)global::System.Collections.Generic.Comparer<global::System.Guid>.Default.Compare(this.Value, other.Value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Int32 CompareTo(global::System.Object? obj)
  {
    if (obj is null)
      return (global::System.Int32)1;
    if (!(bool)obj is global::AspectTestingSpike.OrderId __comparand)
      throw new global::System.ArgumentException("Object must be of type OrderId.", nameof(obj));
    return (global::System.Int32)this.CompareTo(__comparand);
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.OrderId From(global::System.Guid value)
  {
    return new global::AspectTestingSpike.OrderId(value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  static global::AspectTestingSpike.OrderId global::CodoMetis.TypeKit.ValueObjects.IValueObjectMaterializer<global::AspectTestingSpike.OrderId, global::System.Guid>.Materialize(global::System.Guid value)
  {
    return new global::AspectTestingSpike.OrderId(value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.OrderId Parse(global::System.String s, global::System.IFormatProvider? provider)
  {
    global::System.ArgumentNullException.ThrowIfNull(s);
    return __FromText((global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.Guid>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var __parsed) ? __parsed : throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<global::AspectTestingSpike.OrderId, global::System.Guid>()));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.OrderId Parse(global::System.ReadOnlySpan<global::System.Char> s, global::System.IFormatProvider? provider)
  {
    return __FromText((global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.Guid>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var __parsed) ? __parsed : throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<global::AspectTestingSpike.OrderId, global::System.Guid>()));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.OrderId Parse(global::System.ReadOnlySpan<global::System.Byte> utf8Text, global::System.IFormatProvider? provider)
  {
    return __FromText(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseUtf8<global::System.Guid>(utf8Text, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var __parsed) ? __parsed : throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<global::AspectTestingSpike.OrderId, global::System.Guid>());
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.String ToString(global::System.String? format, global::System.IFormatProvider? formatProvider)
  {
    return (global::System.String)(string)global::CodoMetis.TypeKit.CompilerServices.GeneratedFormatting.ToString(this.Value, format, formatProvider ?? global::System.Globalization.CultureInfo.InvariantCulture);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Boolean TryFormat(global::System.Span<global::System.Char> destination, out global::System.Int32 charsWritten, global::System.ReadOnlySpan<global::System.Char> format, global::System.IFormatProvider? provider)
  {
    charsWritten = 0;
    return (global::System.Boolean)(bool)global::CodoMetis.TypeKit.CompilerServices.GeneratedFormatting.TryFormat(this.Value, destination, out charsWritten, format, provider ?? global::System.Globalization.CultureInfo.InvariantCulture);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Boolean TryFormat(global::System.Span<global::System.Byte> utf8Destination, out global::System.Int32 bytesWritten, global::System.ReadOnlySpan<global::System.Char> format, global::System.IFormatProvider? provider)
  {
    bytesWritten = 0;
    return (global::System.Boolean)(bool)global::CodoMetis.TypeKit.CompilerServices.GeneratedFormatting.TryFormatUtf8(this.Value, utf8Destination, out bytesWritten, format, provider ?? global::System.Globalization.CultureInfo.InvariantCulture);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] global::System.String? s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] out global::AspectTestingSpike.OrderId result)
  {
    result = default(global::AspectTestingSpike.OrderId);
    return (global::System.Boolean)(bool)(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.Guid>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var innerValue) && __TryFromText(innerValue, out result));
    global::System.Guid __inner;
    try
    {
      __inner = (global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.Guid>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var __parsed) ? __parsed : throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<global::AspectTestingSpike.OrderId, global::System.Guid>());
    }
    catch (global::System.FormatException)
    {
      return false;
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse(global::System.ReadOnlySpan<global::System.Char> s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] out global::AspectTestingSpike.OrderId result)
  {
    result = default(global::AspectTestingSpike.OrderId);
    return (global::System.Boolean)(bool)(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.Guid>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var innerValue) && __TryFromText(innerValue, out result));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse(global::System.ReadOnlySpan<global::System.Byte> utf8Text, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] out global::AspectTestingSpike.OrderId result)
  {
    result = default(global::AspectTestingSpike.OrderId);
    return (global::System.Boolean)(bool)(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseUtf8<global::System.Guid>(utf8Text, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var innerValue) && __TryFromText(innerValue, out result));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private static global::AspectTestingSpike.OrderId __FromJson(global::System.Guid value, global::System.Boolean materialize)
  {
    return new global::AspectTestingSpike.OrderId(value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private static global::AspectTestingSpike.OrderId __FromText(global::System.Guid value)
  {
    return new global::AspectTestingSpike.OrderId(value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private static global::System.Boolean __TryFromText(global::System.Guid value, out global::AspectTestingSpike.OrderId result)
  {
    result = new global::AspectTestingSpike.OrderId(value);
    return (global::System.Boolean)true;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator>(global::AspectTestingSpike.OrderId left, global::AspectTestingSpike.OrderId right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) > 0;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator >=(global::AspectTestingSpike.OrderId left, global::AspectTestingSpike.OrderId right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) >= 0;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator <(global::AspectTestingSpike.OrderId left, global::AspectTestingSpike.OrderId right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) < 0;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator <=(global::AspectTestingSpike.OrderId left, global::AspectTestingSpike.OrderId right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) <= 0;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public partial class OrderIdJsonConverter : global::System.Text.Json.Serialization.JsonConverter<global::AspectTestingSpike.OrderId>, global::CodoMetis.TypeKit.CompilerServices.IStoredJsonConverterSource
  {
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    private global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.Guid>? __jsonPlan;
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    private readonly global::System.Boolean _materialize;
    public OrderIdJsonConverter()
    {
    }
    private OrderIdJsonConverter(global::System.Boolean materialize)
    {
      this._materialize = materialize;
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    global::System.Text.Json.Serialization.JsonConverter global::CodoMetis.TypeKit.CompilerServices.IStoredJsonConverterSource.CreateStoredJsonConverter(global::CodoMetis.TypeKit.ValueObjects.StoredJsonConverterFactory factory)
    {
      global::System.ArgumentNullException.ThrowIfNull(factory);
      return new global::AspectTestingSpike.OrderId.OrderIdJsonConverter(true);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::AspectTestingSpike.OrderId Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
      if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null)
        throw (global::System.Exception)global::CodoMetis.TypeKit.CompilerServices.GeneratedJson.NullToken<global::AspectTestingSpike.OrderId>();
      return global::AspectTestingSpike.OrderId.__FromJson((this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.Guid>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GuidConverter)).Read<global::AspectTestingSpike.OrderId>(ref reader, options), (global::System.Boolean)this._materialize);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::AspectTestingSpike.OrderId ReadAsPropertyName(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
      return global::AspectTestingSpike.OrderId.__FromJson((this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.Guid>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GuidConverter)).ReadKey<global::AspectTestingSpike.OrderId>(ref reader, options), (global::System.Boolean)this._materialize);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, global::AspectTestingSpike.OrderId value, global::System.Text.Json.JsonSerializerOptions options)
    {
      (this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.Guid>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GuidConverter)).Write(writer, value!.Value, options);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override void WriteAsPropertyName(global::System.Text.Json.Utf8JsonWriter writer, global::AspectTestingSpike.OrderId value, global::System.Text.Json.JsonSerializerOptions options)
    {
      (this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.Guid>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GuidConverter)).WriteKey(writer, value!.Value, options);
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public partial class OrderIdTypeConverter : global::System.ComponentModel.TypeConverter
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
        global::System.IFormatProvider provider;
        if (culture == null)
        {
          provider = global::System.Globalization.CultureInfo.InvariantCulture;
        }
        else
        {
          provider = culture;
        }
        return global::AspectTestingSpike.OrderId.Parse(s, provider);
      }
      return (global::System.Object? )base.ConvertFrom(context, culture, value);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::System.Object? ConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, global::System.Object? value, global::System.Type destinationType)
    {
      if (destinationType == typeof(global::System.String) && value is not null)
      {
        global::System.IFormatProvider provider;
        if (culture == null)
        {
          provider = global::System.Globalization.CultureInfo.InvariantCulture;
        }
        else
        {
          provider = culture;
        }
        return global::CodoMetis.TypeKit.CompilerServices.GeneratedFormatting.ToString(((global::AspectTestingSpike.OrderId)value).Value, null, provider);
      }
      return (global::System.Object? )base.ConvertTo(context, culture, value, destinationType);
    }
  }
  public override readonly global::System.String ToString()
  {
    return (global::System.String)this.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture);
  }
}