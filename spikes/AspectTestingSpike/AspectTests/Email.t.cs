using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
namespace AspectTestingSpike;
public enum EmailFault
{
  Blank,
  NoAt
}
[global::CodoMetis.TypeKit.CompilerServices.GeneratedValueObjectAttribute<global::AspectTestingSpike.Email, global::System.String>]
[global::System.Text.Json.Serialization.JsonConverterAttribute(typeof(global::AspectTestingSpike.Email.EmailJsonConverter))]
[global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
[global::System.ComponentModel.TypeConverterAttribute(typeof(global::AspectTestingSpike.Email.EmailTypeConverter))]
public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>, global::CodoMetis.TypeKit.ValueObjects.IValueObjectMaterializer<global::AspectTestingSpike.Email, global::System.String>, global::CodoMetis.TypeKit.ValueObjects.IValueObject<global::AspectTestingSpike.Email, global::System.String>, global::System.Numerics.IEqualityOperators<global::AspectTestingSpike.Email, global::AspectTestingSpike.Email, global::System.Boolean>, global::System.IParsable<global::AspectTestingSpike.Email>, global::System.ISpanParsable<global::AspectTestingSpike.Email>, global::System.IFormattable, global::System.IComparable<global::AspectTestingSpike.Email>, global::System.IComparable, global::System.Numerics.IComparisonOperators<global::AspectTestingSpike.Email, global::AspectTestingSpike.Email, global::System.Boolean>, global::System.IConvertible
{
  public static Result<Email, EmailFault> Create(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return Result.Error(EmailFault.Blank);
    if (!value.Contains('@'))
      return Result.Error(EmailFault.NoAt);
    return new Email(value.Trim());
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  private readonly global::System.String _value;
  [global::System.Diagnostics.DebuggerHiddenAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  public Email()
  {
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private Email(global::System.String value)
  {
    this._value = value;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.String Value
  {
    get
    {
      return (global::System.String)this._value;
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Int32 CompareTo(global::AspectTestingSpike.Email other)
  {
    return (global::System.Int32)(int)global::System.String.CompareOrdinal(this.Value, other.Value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.Int32 CompareTo(global::System.Object? obj)
  {
    if (obj is null)
      return (global::System.Int32)1;
    if (!(bool)obj is global::AspectTestingSpike.Email __comparand)
      throw new global::System.ArgumentException("Object must be of type Email.", nameof(obj));
    return (global::System.Int32)this.CompareTo(__comparand);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.Email FromKnownGood(global::System.String value, [global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute("value")] global::System.String? source = null)
  {
    return (global::AspectTestingSpike.Email)global::CodoMetis.TypeKit.CompilerServices.GeneratedFactories.OrInvalidOperationException(global::AspectTestingSpike.Email.Create(value), source);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.TypeCode global::System.IConvertible.GetTypeCode()
  {
    return (global::System.TypeCode)global::System.Convert.GetTypeCode((object? )this.Value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  static global::AspectTestingSpike.Email global::CodoMetis.TypeKit.ValueObjects.IValueObjectMaterializer<global::AspectTestingSpike.Email, global::System.String>.Materialize(global::System.String value)
  {
    return new global::AspectTestingSpike.Email(value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.Email Parse(global::System.String s, global::System.IFormatProvider? provider)
  {
    global::System.ArgumentNullException.ThrowIfNull(s);
    return __FromText(s);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::AspectTestingSpike.Email Parse(global::System.ReadOnlySpan<global::System.Char> s, global::System.IFormatProvider? provider)
  {
    return __FromText((global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.String>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var __parsed) ? __parsed : throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<global::AspectTestingSpike.Email, global::System.String>()));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::CodoMetis.TypeKit.Result<global::AspectTestingSpike.Email, global::AspectTestingSpike.EmailFault> Revalidate()
  {
    return (global::CodoMetis.TypeKit.Result<global::AspectTestingSpike.Email, global::AspectTestingSpike.EmailFault>)global::AspectTestingSpike.Email.Create(this.Value);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Boolean global::System.IConvertible.ToBoolean(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToBoolean((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Byte global::System.IConvertible.ToByte(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToByte((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Char global::System.IConvertible.ToChar(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToChar((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.DateTime global::System.IConvertible.ToDateTime(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToDateTime((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Decimal global::System.IConvertible.ToDecimal(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToDecimal((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Double global::System.IConvertible.ToDouble(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToDouble((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Int16 global::System.IConvertible.ToInt16(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToInt16((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Int32 global::System.IConvertible.ToInt32(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToInt32((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Int64 global::System.IConvertible.ToInt64(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToInt64((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.SByte global::System.IConvertible.ToSByte(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToSByte((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Single global::System.IConvertible.ToSingle(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToSingle((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.String global::System.IConvertible.ToString(global::System.IFormatProvider? provider)
  {
    return (global::System.String)(string)global::System.Convert.ToString((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public global::System.String ToString(global::System.String? format, global::System.IFormatProvider? formatProvider)
  {
    return (global::System.String)(string)(this.Value ?? string.Empty);
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.Object global::System.IConvertible.ToType(global::System.Type conversionType, global::System.IFormatProvider? provider)
  {
    if ((bool)(conversionType == typeof(global::AspectTestingSpike.Email) || conversionType == typeof(object)))
      return this;
    if (conversionType == typeof(global::System.String))
      return global::System.Convert.ToString((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
    return global::System.Convert.ChangeType((object? )this.Value, conversionType, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture))!;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.UInt16 global::System.IConvertible.ToUInt16(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToUInt16((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.UInt32 global::System.IConvertible.ToUInt32(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToUInt32((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  global::System.UInt64 global::System.IConvertible.ToUInt64(global::System.IFormatProvider? provider)
  {
    return global::System.Convert.ToUInt64((object? )this.Value, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::CodoMetis.TypeKit.Option<global::AspectTestingSpike.Email> TryFrom(global::System.String value)
  {
    return (global::CodoMetis.TypeKit.Option<global::AspectTestingSpike.Email>)global::CodoMetis.TypeKit.Result.ToOption(global::AspectTestingSpike.Email.Create(value));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] global::System.String? s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] out global::AspectTestingSpike.Email result)
  {
    result = default(global::AspectTestingSpike.Email);
    return (global::System.Boolean)(bool)(s is not null && __TryFromText(s, out result));
    global::System.String __inner;
    try
    {
      __inner = s;
    }
    catch (global::System.FormatException)
    {
      return false;
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean TryParse(global::System.ReadOnlySpan<global::System.Char> s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] out global::AspectTestingSpike.Email result)
  {
    result = default(global::AspectTestingSpike.Email);
    return (global::System.Boolean)(bool)(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<global::System.String>(s, (provider ?? global::System.Globalization.CultureInfo.InvariantCulture), out var innerValue) && __TryFromText(innerValue, out result));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private static global::AspectTestingSpike.Email __FromJson(global::System.String value, global::System.Boolean materialize)
  {
    if (materialize)
      return new global::AspectTestingSpike.Email(value);
    return (global::AspectTestingSpike.Email)global::CodoMetis.TypeKit.CompilerServices.GeneratedFactories.OrJsonException(global::AspectTestingSpike.Email.Create(value));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private static global::AspectTestingSpike.Email __FromText(global::System.String value)
  {
    return (global::AspectTestingSpike.Email)global::CodoMetis.TypeKit.CompilerServices.GeneratedFactories.OrFormatException(global::AspectTestingSpike.Email.Create(value));
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
  [global::System.Diagnostics.DebuggerStepThroughAttribute]
  private static global::System.Boolean __TryFromText(global::System.String value, out global::AspectTestingSpike.Email result)
  {
    result = default(global::AspectTestingSpike.Email);
    return (global::System.Boolean)(bool)global::AspectTestingSpike.Email.Create(value).TryGetValue(out result, out _);
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator>(global::AspectTestingSpike.Email left, global::AspectTestingSpike.Email right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) > 0;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator >=(global::AspectTestingSpike.Email left, global::AspectTestingSpike.Email right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) >= 0;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator <(global::AspectTestingSpike.Email left, global::AspectTestingSpike.Email right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) < 0;
  }
  [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public static global::System.Boolean operator <=(global::AspectTestingSpike.Email left, global::AspectTestingSpike.Email right)
  {
    return (global::System.Boolean)(bool)left.CompareTo(right) <= 0;
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public partial class EmailJsonConverter : global::System.Text.Json.Serialization.JsonConverter<global::AspectTestingSpike.Email>, global::CodoMetis.TypeKit.CompilerServices.IStoredJsonConverterSource
  {
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    private global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.String>? __jsonPlan;
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    private readonly global::System.Boolean _materialize;
    public EmailJsonConverter()
    {
    }
    private EmailJsonConverter(global::System.Boolean materialize)
    {
      this._materialize = materialize;
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    global::System.Text.Json.Serialization.JsonConverter global::CodoMetis.TypeKit.CompilerServices.IStoredJsonConverterSource.CreateStoredJsonConverter(global::CodoMetis.TypeKit.ValueObjects.StoredJsonConverterFactory factory)
    {
      global::System.ArgumentNullException.ThrowIfNull(factory);
      return new global::AspectTestingSpike.Email.EmailJsonConverter(true);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::AspectTestingSpike.Email Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
      if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null)
        throw (global::System.Exception)global::CodoMetis.TypeKit.CompilerServices.GeneratedJson.NullToken<global::AspectTestingSpike.Email>();
      return global::AspectTestingSpike.Email.__FromJson((this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.String>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.StringConverter)).Read<global::AspectTestingSpike.Email>(ref reader, options), (global::System.Boolean)this._materialize);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override global::AspectTestingSpike.Email ReadAsPropertyName(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
      return global::AspectTestingSpike.Email.__FromJson((this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.String>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.StringConverter)).ReadKey<global::AspectTestingSpike.Email>(ref reader, options), (global::System.Boolean)this._materialize);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, global::AspectTestingSpike.Email value, global::System.Text.Json.JsonSerializerOptions options)
    {
      (this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.String>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.StringConverter)).Write(writer, value!.Value, options);
    }
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    public override void WriteAsPropertyName(global::System.Text.Json.Utf8JsonWriter writer, global::AspectTestingSpike.Email value, global::System.Text.Json.JsonSerializerOptions options)
    {
      (this.__jsonPlan is { } __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : this.__jsonPlan = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<global::System.String>.For(options, global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.StringConverter)).WriteKey(writer, value!.Value, options);
    }
  }
  [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
  public partial class EmailTypeConverter : global::System.ComponentModel.TypeConverter
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
        return global::AspectTestingSpike.Email.Parse(s, provider);
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
        return (((global::AspectTestingSpike.Email)value).Value ?? string.Empty);
      }
      return (global::System.Object? )base.ConvertTo(context, culture, value, destinationType);
    }
  }
  public override readonly global::System.String ToString()
  {
    return (global::System.String)this.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture);
  }
}