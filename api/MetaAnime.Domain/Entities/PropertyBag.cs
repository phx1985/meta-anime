namespace MetaAnime.Domain.Entities;

public class PropertyBag
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime CreationDate { get; set; }
    public DateTime UpdateDate { get; set; }
}
