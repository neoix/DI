using System.Text.Json;

namespace DI.ApiService.Extensions;

public static class SerializerExtension
{
  private static readonly JsonSerializerOptions options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  public static string AsString<T>(this T obj)
  {    
    return JsonSerializer.Serialize(obj, options);
  }

  public static T? AsObject<T>(this string json)
  {
    return JsonSerializer.Deserialize<T>(json, options);
  }
}
