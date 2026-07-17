using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public enum MarketingStatus { Draft, Active, Archived }
public enum ContentType { Photo, Reels, Stories, Carousel, Review, Unboxing }
public enum ContentStatus { Idea, Planned, Ready, Published, Cancelled }

public sealed class ProductCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Season { get; set; }
    public string? Style { get; set; }
    public decimal? BudgetLimitUzs { get; set; }
    public MarketingStatus? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CollectionProduct> Products { get; set; } = [];
}

public sealed class CollectionProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class Outfit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название образа")] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Occasion { get; set; }
    public MarketingStatus? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OutfitProduct> Products { get; set; } = [];
    public decimal TotalPriceUzs => Products.Sum(x => x.SellingPriceUzs ?? 0);
}

public sealed class OutfitProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal? SellingPriceUzs { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ContentPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите тему публикации")] public string Title { get; set; } = string.Empty;
    public ContentType? Type { get; set; }
    public ContentStatus? Status { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public Guid? CollectionId { get; set; }
    public string? CollectionName { get; set; }
    public Guid? OutfitId { get; set; }
    public string? OutfitName { get; set; }
    public string? Caption { get; set; }
    public string? PublicationUrl { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class MarketingText
{
    public static string Display(this MarketingStatus? value) => value switch { MarketingStatus.Draft => "Черновик", MarketingStatus.Active => "Активна", MarketingStatus.Archived => "Архив", _ => "Не указан" };
    public static string Display(this MarketingStatus value) => ((MarketingStatus?)value).Display();
    public static string Display(this ContentType value) => value switch { ContentType.Photo => "Фото", ContentType.Reels => "Reels", ContentType.Stories => "Stories", ContentType.Carousel => "Карусель", ContentType.Review => "Обзор", ContentType.Unboxing => "Распаковка", _ => value.ToString() };
    public static string Display(this ContentStatus? value) => value switch { ContentStatus.Idea => "Идея", ContentStatus.Planned => "Запланировано", ContentStatus.Ready => "Готово", ContentStatus.Published => "Опубликовано", ContentStatus.Cancelled => "Отменено", _ => "Не указан" };
    public static string Display(this ContentStatus value) => ((ContentStatus?)value).Display();
}
