using System.Security.Cryptography.X509Certificates;
using WordList.Data.Sql;
using WordList.Data.Sql.Models;

namespace WordList.Processing.UpdateBatch;

public class AttributeParser
{
    private List<string> s_attributeNames = [];
    private Dictionary<string, WordAttribute>? s_attributes;

    public async Task LoadAttributesAsync()
    {
        if (s_attributes is null)
        {
            s_attributes = (await WordAttributes
                .GetAllAsync()
                .ConfigureAwait(false)
            ).ToDictionary(x => x.Name, x => x);

            s_attributeNames = s_attributes.Keys.OrderBy(k => k).ToList();
        }
    }

    public Dictionary<string, int> ParseAttributesFromResponse(string[] responseItems, int firstIndex, int lastIndex)
    {
        if (s_attributes is null)
        {
            throw new InvalidOperationException("Attributes have not been loaded. Call LoadAttributesAsync first.");
        }

        if (firstIndex < 0
            || firstIndex >= responseItems.Length
            || lastIndex < firstIndex
            || lastIndex >= responseItems.Length
            || firstIndex + lastIndex > responseItems.Length)
        {
            throw new InvalidOperationException("Indices are out of range.");
        }

        if (firstIndex + lastIndex != s_attributeNames.Count)
        {
            throw new InvalidDataException($"Wrong number of attribute items.  Expected: {s_attributeNames.Count}, got: {firstIndex + lastIndex}");
        }

        var attributes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < s_attributeNames.Count; i++)
        {
            var attributeName = s_attributeNames[i];
            var attributeValue = responseItems[firstIndex + i];

            if (int.TryParse(attributeValue, out int value))
            {
                var attribute = s_attributes[attributeName];
                if (value < attribute.Min || value > attribute.Max)
                {
                    throw new InvalidDataException(
                        $"Value of '{attributeValue}' for attribute '{attributeName}' is out of range: {value} (expected {attribute.Min} to {attribute.Max})"
                    );
                }

                attributes[attributeName] = value;
            }
            else
            {
                throw new FormatException($"Invalid format for attribute '{attributeName}': '{attributeValue}' (expected integer)");
            }
        }

        return attributes;
    }
}