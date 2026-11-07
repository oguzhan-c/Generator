using CanFx.Generator.Engine;

namespace CanFx.Generator.Tests;

public sealed class NamesTests
{
    [Theory]
    [InlineData("Brand", "Brands")]
    [InlineData("Category", "Categories")]
    [InlineData("Box", "Boxes")]
    [InlineData("Address", "Addresses")]
    [InlineData("Day", "Days")]
    [InlineData("Branch", "Branches")]
    public void Plural(string name, string expected) => Assert.Equal(expected, Names.Plural(name));

    [Theory]
    [InlineData("ProductCategories", "product-categories")]
    [InlineData("Brand", "brand")]
    [InlineData("SKUItem", "sku-item")]
    public void Kebab(string name, string expected) => Assert.Equal(expected, Names.Kebab(name));

    [Fact]
    public void Snake_and_words()
    {
        Assert.Equal("product_category", Names.Snake("ProductCategory"));
        Assert.Equal("Product category", Names.Words("ProductCategory"));
    }
}

public sealed class EntityReaderTests
{
    private const string Brand = """
        using CanFx.Domain.Auditing;
        using CanFx.Domain.Results;

        namespace Shop.Domain.Catalog;

        public enum BrandTier { Standard, Premium }

        [Audited]
        public sealed class Brand : FullAuditedAggregateRoot<Guid>, IMultiTenant<Guid?>
        {
            public const int NameMaxLength = 60;

            private Brand() { Name = string.Empty; }

            public string Name { get; private set; }
            public string? Description { get; private set; }
            public decimal Rating { get; private set; }
            public BrandTier Tier { get; private set; }
            public Guid? TenantId { get; private set; }
            public IReadOnlyCollection<string> Tags => _tags;
            public List<BrandLogo> Logos { get; private set; } = [];
            public Money Price { get; private set; } = null!;
            public string Display => Name;

            public static Result<Brand> Create(string name, string? description, BrandTier tier) => new Brand { Name = name };

            public Result<Success> Update(string name, string? description) => Result.Success;
        }
        """;

    [Fact]
    public void Reads_entity_shape()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", Brand);

        EntityModel model = EntityReader.Read(SolutionLayout.Find(solution.Root), "Brand");

        Assert.Equal("Shop.Domain.Catalog", model.Namespace);
        Assert.Equal("Guid", model.IdType);
        Assert.True(model.IsAudited);
        Assert.True(model.IsSoftDeletable);
        Assert.True(model.IsMultiTenant);
        Assert.Equal(new[] { "Name", "Description", "Rating", "Tier", "TenantId", "Logos", "Price" }, model.Properties.Select(p => p.Name));
        Assert.Equal(PropertyKind.Enum, model.Properties.Single(p => p.Name == "Tier").Kind);
        Assert.Equal(PropertyKind.Collection, model.Properties.Single(p => p.Name == "Logos").Kind);
        Assert.Equal(PropertyKind.Complex, model.Properties.Single(p => p.Name == "Price").Kind);
        Assert.Equal("NameMaxLength", model.MaxLengthConstant("name"));
        Assert.True(model.Factory!.ReturnsResult);
        Assert.Equal(new[] { "name", "description", "tier" }, model.Factory.Parameters.Select(p => p.Name));
        Assert.Equal(2, model.Update!.Parameters.Count);
    }

    [Fact]
    public void Reads_entity_by_file_and_lists_entities()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", Brand);
        solution.Write("src/Shop.Domain/Tags/Tag.cs", "namespace Shop.Domain.Tags; public class Tag : Entity<int> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Path_("src/Shop.Domain/Tags"));

        Assert.Equal("Tag", EntityReader.Read(layout, solution.Path_("src/Shop.Domain/Tags/Tag.cs")).Name);
        Assert.Equal(new[] { "Brand", "Tag" }, EntityReader.List(layout).Select(e => e.Name));
    }

    [Fact]
    public void Missing_entity_is_reported()
    {
        using var solution = new TestSolution();
        Assert.Throws<GeneratorException>(() => EntityReader.Read(SolutionLayout.Find(solution.Root), "Nope"));
    }
}

public sealed class CrudGeneratorTests
{
    private const string Feature = "src/Shop.Application/Features/Brands/";

    [Fact]
    public void Recognizes_legacy_can_gen_markers_of_solutions_created_with_0_1_0()
    {
        using var solution = new TestSolution();
        foreach (string file in Directory.GetFiles(solution.Root, "*.cs", SearchOption.AllDirectories))
            File.WriteAllText(file, File.ReadAllText(file).Replace("// canfxgen:", "// can-gen:", StringComparison.Ordinal));
        solution.Write("src/Shop.Domain/Entities/Brand.cs", """
            namespace Shop.Domain.Entities;

            public sealed class Brand : FullAuditedAggregateRoot<Guid>
            {
                public string Name { get; set; } = "";
            }
            """);

        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        IReadOnlyList<ChangeResult> results = CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();

        Assert.DoesNotContain(results, r => r.Kind == ChangeKind.Failed);
        string context = solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs").ReplaceLineEndings("\n");
        Assert.Contains("    public DbSet<Brand> Brands => Set<Brand>();\n    // can-gen:dbsets", context);
        Assert.Contains("services.AddScoped<IBrandRepository, BrandRepository>();", solution.Read("src/Shop.Persistence/PersistenceServiceRegistration.cs"));
    }

    [Fact]
    public void Generates_narchitecture_layout_and_injects_registrations_once()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", """
            namespace Shop.Domain.Entities;

            public sealed class Brand : FullAuditedAggregateRoot<Guid>
            {
                public const int NameMaxLength = 60;
                private Brand() { }
                public string Name { get; private set; } = "";
                public bool IsActive { get; private set; }
                public static Result<Brand> Create(string name) => new Brand();
                public void Update(string name, bool isActive) { }
            }
            """);

        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        IReadOnlyList<ChangeResult> first = CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();

        Assert.DoesNotContain(first, r => r.Kind == ChangeKind.Failed);
        foreach (string file in new[]
        {
            "Commands/Create/CreateBrandCommand.cs", "Commands/Create/CreateBrandCommandValidator.cs", "Commands/Create/CreatedBrandResponse.cs",
            "Commands/Update/UpdateBrandCommand.cs", "Commands/Update/UpdateBrandCommandValidator.cs", "Commands/Update/UpdatedBrandResponse.cs",
            "Commands/Delete/DeleteBrandCommand.cs", "Commands/Delete/DeletedBrandResponse.cs",
            "Queries/GetById/GetByIdBrandQuery.cs", "Queries/GetById/GetByIdBrandResponse.cs",
            "Queries/GetList/GetListBrandQuery.cs", "Queries/GetList/GetListBrandQueryValidator.cs", "Queries/GetList/GetListBrandListItemDto.cs",
            "Queries/GetListByDynamic/GetListByDynamicBrandQuery.cs", "Queries/GetListByDynamic/GetListByDynamicBrandQueryValidator.cs",
            "Constants/BrandsBusinessMessages.cs", "Constants/BrandsOperationClaims.cs",
            "Commands/Delete/DeleteBrandCommandValidator.cs", "Queries/GetListByDynamic/GetListByDynamicBrandListItemDto.cs",
            "Profiles/MappingProfiles.cs", "Rules/BrandBusinessRules.cs",
            "Commands/Create/CreateBrandCommandHandler.cs", "Commands/Update/UpdateBrandCommandHandler.cs",
            "Commands/Delete/DeleteBrandCommandHandler.cs", "Queries/GetById/GetByIdBrandQueryHandler.cs",
            "Queries/GetList/GetListBrandQueryHandler.cs", "Queries/GetListByDynamic/GetListByDynamicBrandQueryHandler.cs",
        })
        {
            Assert.True(solution.Exists(Feature + file), file);
        }

        Assert.True(solution.Exists("src/Shop.Application/Services/Repositories/IBrandRepository.cs"));
        Assert.True(solution.Exists("src/Shop.Persistence/Repositories/BrandRepository.cs"));
        Assert.True(solution.Exists("src/Shop.Persistence/EntityConfigurations/BrandConfiguration.cs"));
        Assert.True(solution.Exists("src/Shop.WebAPI/Controllers/BrandsController.cs"));
        Assert.True(solution.Exists("src/Shop.Application/Services/Brands/IBrandService.cs"));
        Assert.True(solution.Exists("src/Shop.Application/Services/Brands/BrandManager.cs"));
        Assert.Contains("services.AddScoped<IBrandService, BrandManager>();", solution.Read("src/Shop.Application/ApplicationServiceRegistration.cs"));
        Assert.Contains("ISecuredRequest, ILoggableRequest, ITransactionalRequest", ReadWithHandler(solution, Feature + "Commands/Create/CreateBrandCommand.cs"));
        Assert.Contains("ICachableRequest", ReadWithHandler(solution, Feature + "Queries/GetList/GetListBrandQuery.cs"));

        // Dosya başına tek tip: istek dosyasında handler yok, handler kendi dosyasında ve yalnızca gerektiği using'lerle.
        string request = solution.Read(Feature + "Commands/Create/CreateBrandCommand.cs");
        string handler = solution.Read(Feature + "Commands/Create/CreateBrandCommandHandler.cs");
        Assert.DoesNotContain("Handler", request);
        Assert.DoesNotContain("using CanFx.Mapping;", request);
        Assert.Contains("public sealed class CreateBrandCommandHandler : IRequestHandler<CreateBrandCommand, CreatedBrandResponse>", handler);
        Assert.Contains("using CanFx.Mapping;", handler);
        Assert.DoesNotContain("using System.Text.Json.Serialization;", handler);
        Assert.StartsWith("// canfxgen ile üretildi", handler);
        Assert.Contains("// canfxgen:endpoints", solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs"));

        string create = ReadWithHandler(solution, Feature + "Commands/Create/CreateBrandCommand.cs");
        Assert.Contains("Brand brand = Brand.Create(request.Name).OrThrow();", create);
        Assert.Contains("public string Name { get; set; } = string.Empty;", create);
        Assert.Contains("[BrandsOperationClaims.Admin, BrandsOperationClaims.Write, BrandsOperationClaims.Create]", create);
        Assert.DoesNotContain("SaveChangesAsync", create); // Guid kimlik kodda üretilir
        Assert.Contains("brand.Update(request.Name, request.IsActive);", ReadWithHandler(solution, Feature + "Commands/Update/UpdateBrandCommand.cs"));
        Assert.Contains("RuleFor(x => x.Name).NotEmpty().MaximumLength(Brand.NameMaxLength);", solution.Read(Feature + "Commands/Create/CreateBrandCommandValidator.cs"));
        Assert.Contains("throw new NotFoundException(BrandsBusinessMessages.BrandNotExists)", solution.Read(Feature + "Rules/BrandBusinessRules.cs"));
        Assert.DoesNotContain("CreateMap<CreateBrandCommand, Brand>", solution.Read(Feature + "Profiles/MappingProfiles.cs"));
        Assert.Contains("public DateTimeOffset CreatedAt { get; set; }", solution.Read(Feature + "Queries/GetById/GetByIdBrandResponse.cs"));
        Assert.Contains("[Route(\"api/v1/[controller]\")]", solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs"));
        Assert.Contains("builder.Property(x => x.Name).HasColumnName(\"Name\").HasMaxLength(Brand.NameMaxLength).IsRequired();", solution.Read("src/Shop.Persistence/EntityConfigurations/BrandConfiguration.cs"));

        string context = solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs");
        Assert.Contains("    public DbSet<Brand> Brands => Set<Brand>();\n    // canfxgen:dbsets", context.ReplaceLineEndings("\n"));
        Assert.Contains("using Shop.Domain.Entities;", context);

        string registration = solution.Read("src/Shop.Persistence/PersistenceServiceRegistration.cs");
        Assert.Contains("services.AddScoped<IBrandRepository, BrandRepository>();", registration);
        Assert.Contains("using Shop.Persistence.Repositories;", registration);

        string claims = solution.Read("src/Shop.Application/Services/OperationClaims/AppOperationClaims.cs");
        Assert.Contains("..BrandsOperationClaims.All,", claims);
        Assert.Contains("using Shop.Application.Features.Brands.Constants;", claims);

        // Metinler özelliğin kendi dosyalarında; ortak dosyada çevirisi olan alan (Name) tekrar yazılmaz.
        string tr = solution.Read(Feature + "Localization/brands.tr.json").ReplaceLineEndings("\n");
        Assert.Contains("\"brands\": {\n    \"brand_not_exists\": \"Brand kaydı bulunamadı.\"", tr);
        Assert.DoesNotContain("\"Name\"", tr);
        Assert.Contains("\"brand_not_exists\": \"Brand not found.\"", solution.Read(Feature + "Localization/brands.en.json"));
        Assert.Contains("public const string BrandNotExists = \"brands.brand_not_exists\";", solution.Read(Feature + "Constants/BrandsBusinessMessages.cs"));
        Assert.Contains("{ Code = BrandsBusinessMessages.BrandNotExists }", solution.Read(Feature + "Rules/BrandBusinessRules.cs"));

        // İkinci çalıştırma: dosyalar korunur, eklemeler tekrarlanmaz.
        IReadOnlyList<ChangeResult> second = CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();
        Assert.All(second, r => Assert.Contains(r.Kind, new[] { ChangeKind.Skipped, ChangeKind.Unchanged }));
        Assert.Single(solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs").Split("DbSet<Brand>")[1..]);
    }

    [Fact]
    public void Uses_mapper_for_public_constructor_and_setters()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Color.cs", """
            namespace Shop.Domain.Entities;

            public class Color : FullAuditedEntity<int>
            {
                public string ColorName { get; set; }

                public Color(string colorName) => ColorName = colorName;
            }
            """);

        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Color")).Apply();

        string feature = "src/Shop.Application/Features/Colors/";
        string create = ReadWithHandler(solution, feature + "Commands/Create/CreateColorCommand.cs");
        Assert.Contains("Color color = _mapper.Map<Color>(request);", create);
        Assert.Contains("await _unitOfWork.SaveChangesAsync(cancellationToken);", create); // int kimlik veritabanında
        Assert.Contains("_mapper.Map(request, color);", ReadWithHandler(solution, feature + "Commands/Update/UpdateColorCommand.cs"));

        string profiles = solution.Read(feature + "Profiles/MappingProfiles.cs");
        Assert.Contains("CreateMap<CreateColorCommand, Color>();", profiles);
        Assert.Contains("CreateMap<UpdateColorCommand, Color>().ForMember(x => x.Id, o => o.Ignore());", profiles);
        Assert.Contains("\"{Id}\"", solution.Read("src/Shop.WebAPI/Controllers/ColorsController.cs"));
        Assert.Contains(".OrderByDescending(x => x.CreatedAt)", ReadWithHandler(solution, feature + "Queries/GetList/GetListColorQuery.cs"));
    }

    [Fact]
    public void Leaves_todos_when_entity_cannot_be_created_or_updated()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Tag.cs", """
            namespace Shop.Domain.Entities;

            public class Tag : Entity<int>
            {
                private Tag() { }
                public string Name { get; private set; } = "";
            }
            """);

        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Tag")).Apply();

        string create = ReadWithHandler(solution, "src/Shop.Application/Features/Tags/Commands/Create/CreateTagCommand.cs");
        Assert.Contains("TODO(canfxgen): Tag'de fabrika metodu", create);
        Assert.Contains("Tag tag = NewTag(request);", create);
        Assert.Contains("TODO(canfxgen): Tag'de 'Update(...)' metodu ya da public setter'lar bulunamadı", ReadWithHandler(solution, "src/Shop.Application/Features/Tags/Commands/Update/UpdateTagCommand.cs"));
        Assert.Contains(".OrderBy(x => x.Id)", ReadWithHandler(solution, "src/Shop.Application/Features/Tags/Queries/GetList/GetListTagQuery.cs"));
    }

    [Fact]
    public void Dry_run_writes_nothing_and_force_overwrites()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Tag.cs", "namespace Shop.Domain.Entities; public class Tag : Entity<int> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);

        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Tag")).Apply(dryRun: true);
        Assert.False(solution.Exists("src/Shop.WebAPI/Controllers/TagsController.cs"));
        Assert.DoesNotContain("DbSet<Tag>", solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs"));

        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Tag")).Apply();
        solution.Write("src/Shop.WebAPI/Controllers/TagsController.cs", "// değiştirildi");
        IReadOnlyList<ChangeResult> forced = CrudGenerator.Plan(layout, EntityReader.Read(layout, "Tag")).Apply(force: true);

        Assert.Contains(forced, r => r.Kind == ChangeKind.Overwritten && r.Path.EndsWith("TagsController.cs", StringComparison.Ordinal));
        Assert.Contains("class TagsController", solution.Read("src/Shop.WebAPI/Controllers/TagsController.cs"));
    }

    [Fact]
    public void Mechanisms_can_be_turned_off()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);

        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand"), new CrudOptions { Caching = false, Logging = false, Transaction = false, Secured = false, DynamicQuery = false }).Apply();

        string create = ReadWithHandler(solution, Feature + "Commands/Create/CreateBrandCommand.cs");
        Assert.Contains("public sealed class CreateBrandCommand : IRequest<CreatedBrandResponse>\n", create.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("Permissions", create);
        Assert.Contains("transaction kapalı: handler kendisi kaydeder", create);
        Assert.Contains("await _unitOfWork.SaveChangesAsync(cancellationToken);", ReadWithHandler(solution, Feature + "Commands/Delete/DeleteBrandCommand.cs"));
        Assert.DoesNotContain("ICachableRequest", ReadWithHandler(solution, Feature + "Queries/GetList/GetListBrandQuery.cs"));
        Assert.False(solution.Exists(Feature + "Queries/GetListByDynamic/GetListByDynamicBrandQuery.cs"));
        Assert.DoesNotContain("GetList/ByDynamic", solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs"));

        // Dinamik sorgu sonradan eklenebilir.
        CrudGenerator.PlanDynamicQuery(layout, EntityReader.Read(layout, "Brand")).Apply();
        Assert.True(solution.Exists(Feature + "Queries/GetListByDynamic/GetListByDynamicBrandQuery.cs"));
        string controller = solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs");
        Assert.Contains("[HttpPost(\"GetList/ByDynamic\")]", controller);
        Assert.Contains("[AcceptVerbs(\"QUERY\", Route = \"GetList/ByDynamic\")]", controller);
        Assert.Equal(1, controller.Split("public Task<IActionResult> QueryListByDynamic").Length - 1);
        Assert.Contains("using Shop.Application.Features.Brands.Queries.GetListByDynamic;", controller);
        Assert.Contains("CreateMap<Brand, GetListByDynamicBrandListItemDto>();", solution.Read(Feature + "Profiles/MappingProfiles.cs"));
    }

    [Fact]
    public void Adds_command_and_query_to_existing_feature()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();

        Assert.Equal(new[] { "Brands" }, FeatureGenerator.ListFeatures(layout));

        IReadOnlyList<ChangeResult> command = FeatureGenerator.PlanCommand(layout, "Brands", "SyncBrandCommand", new RequestOptions { EndpointMethod = "put" }).Apply();
        Assert.DoesNotContain(command, r => r.Kind == ChangeKind.Failed);
        string syncFolder = Feature + "Commands/SyncBrand/";
        Assert.True(solution.Exists(syncFolder + "SyncBrandCommand.cs"));
        Assert.True(solution.Exists(syncFolder + "SyncBrandCommandHandler.cs"));
        Assert.True(solution.Exists(syncFolder + "SyncBrandCommandValidator.cs"));
        Assert.True(solution.Exists(syncFolder + "SyncBrandResponse.cs"));
        string sync = solution.Read(syncFolder + "SyncBrandCommand.cs");
        Assert.Contains("[BrandsOperationClaims.Admin, BrandsOperationClaims.Write, BrandsOperationClaims.SyncBrand]", sync);
        Assert.Contains("ICacheRemoverRequest", sync);
        Assert.Contains("CacheTags.Entity<Brand>()", sync);

        FeatureGenerator.PlanQuery(layout, "Brands", "GetBrandByName").Apply();
        Assert.Contains("ICachableRequest", solution.Read(Feature + "Queries/GetBrandByName/GetBrandByNameQuery.cs"));
        Assert.True(solution.Exists(Feature + "Queries/GetBrandByName/GetBrandByNameQueryHandler.cs"));

        string claims = solution.Read(Feature + "Constants/BrandsOperationClaims.cs");
        Assert.Contains("public const string SyncBrand = \"brands.sync-brand\";", claims);
        Assert.Contains("(GetBrandByName, \"Get brand by name\"),", claims);

        string controller = solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs").ReplaceLineEndings("\n");
        Assert.Contains("    [HttpPut(\"SyncBrand\")]\n    public async Task<IActionResult> SyncBrand([FromBody] SyncBrandCommand syncBrandCommand, CancellationToken cancellationToken)", controller);
        Assert.Contains("[HttpGet(\"GetBrandByName\")]", controller);
        Assert.Contains("[FromQuery] GetBrandByNameQuery getBrandByNameQuery", controller);
        Assert.Contains("using Shop.Application.Features.Brands.Commands.SyncBrand;", controller);

        Assert.Throws<GeneratorException>(() => FeatureGenerator.PlanCommand(layout, "Nope", "X"));
    }

    [Fact]
    public void Old_solutions_get_texts_in_webapi_localization()
    {
        using var solution = new TestSolution();
        solution.UseLegacyLocalization();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);

        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();

        Assert.Contains("\"brands\": { \"brand_not_exists\": \"Brand kaydı bulunamadı.\" }", solution.Read("src/Shop.WebAPI/Localization/tr.json"));
        Assert.False(solution.Exists(Feature + "Localization/brands.tr.json"));
    }

    [Fact]
    public void New_entity_lists_base_class_options_in_comments()
    {
        using var solution = new TestSolution();
        SolutionLayout layout = SolutionLayout.Find(solution.Root);

        EntityGenerator.Plan(layout, "brand").Apply();
        EntityGenerator.Plan(layout, "Product", Path.Combine(layout.Domain, "Catalog"), "int").Apply();

        string brand = solution.Read("src/Shop.Domain/Entities/Brand.cs");
        Assert.Contains("namespace Shop.Domain.Entities;", brand);
        Assert.Contains("public class Brand : FullAuditedEntity<Guid>", brand);
        Assert.Contains("//   AuditedEntity<Guid>             Id + CreatedAt, CreatedBy, UpdatedAt, UpdatedBy", brand);
        Assert.Contains("FullAuditedAggregateRoot<Guid>", brand);
        Assert.Contains("IMultiTenant<Guid>", brand);

        string product = solution.Read("src/Shop.Domain/Catalog/Product.cs");
        Assert.Contains("namespace Shop.Domain.Catalog;", product);
        Assert.Contains("public class Product : FullAuditedEntity<int>", product);

        Assert.Equal("Brand", EntityReader.Read(layout, "Brand").Name);
        Assert.Throws<GeneratorException>(() => EntityGenerator.Plan(layout, "2nd"));
        Assert.Throws<GeneratorException>(() => EntityGenerator.Plan(layout, "Order", solution.Root));
    }

    [Fact]
    public void Responses_list_omitted_audit_fields_as_comments()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : FullAuditedEntity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();

        string created = solution.Read(Feature + "Commands/Create/CreatedBrandResponse.cs");
        Assert.Contains("public DateTimeOffset CreatedAt { get; set; }", created);
        Assert.DoesNotContain("public DateTimeOffset? UpdatedAt", created);
        Assert.Contains("// public string? CreatedBy { get; set; }", created);
        Assert.Contains("IsDeleted / DeletedAt / DeletedBy", created);

        string read = solution.Read(Feature + "Queries/GetById/GetByIdBrandResponse.cs");
        Assert.Contains("public DateTimeOffset? UpdatedAt { get; set; }", read);
        Assert.Contains("// public string? UpdatedBy { get; set; }", read);

        Assert.Contains("soft delete", solution.Read(Feature + "Commands/Delete/DeletedBrandResponse.cs"));
    }

    [Fact]
    public void Seeders_read_required_data_from_json_and_add_sample_data_in_development()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Category.cs", """
            namespace Shop.Domain.Entities;

            public class Category : Entity<Guid>
            {
                public string Name { get; set; } = "";
            }
            """);
        solution.Write("src/Shop.Domain/Entities/Product.cs", """
            namespace Shop.Domain.Entities;

            public enum ProductStatus { Draft, Active }

            public class Product : FullAuditedEntity<Guid>
            {
                public const int NameMaxLength = 80;

                private Product() { }

                public string Name { get; private set; } = "";
                public decimal Price { get; private set; }
                public Guid CategoryId { get; private set; }
                public ProductStatus Status { get; private set; }

                public static Product Create(string name, decimal price, Guid categoryId, ProductStatus status) =>
                    new() { Name = name, Price = price, CategoryId = categoryId, Status = status };
            }
            """);
        SolutionLayout layout = SolutionLayout.Find(solution.Root);

        IReadOnlyList<ChangeResult> category = SeederGenerator.Plan(layout, EntityReader.Read(layout, "Category")).Apply();
        Assert.DoesNotContain(category, r => r.Kind == ChangeKind.Failed);
        string categorySeeder = solution.Read("src/Shop.Persistence/Seeding/CategorySeeder.cs");
        Assert.Contains("public int Order => 100;", categorySeeder);
        Assert.Contains("SeedFiles.ReadJsonAsync<CategorySeed>(\"Seeding/Data/categories.json\"", categorySeeder);
        Assert.Contains("!existing.Contains(i.Name)", categorySeeder);
        Assert.Contains("new Category { Name = item.Name }", categorySeeder);
        Assert.Contains("public sealed record CategorySeed(string Name);", solution.Read("src/Shop.Persistence/Seeding/CategorySeed.cs"));
        Assert.Contains("[\n]", solution.Read("src/Shop.Persistence/Seeding/Data/categories.json").ReplaceLineEndings("\n"));
        Assert.Contains(@"<None Update=""Seeding\Data\*.json""", solution.Read("src/Shop.Persistence/Shop.Persistence.csproj"));

        // Product, Category'ye bağlı: sırası ondan sonra; örnek veride kategori var olanlardan seçilir.
        SeederGenerator.Plan(layout, EntityReader.Read(layout, "Product")).Apply();
        string product = solution.Read("src/Shop.Persistence/Seeding/ProductSeeder.cs");
        Assert.Contains("public int Order => 110;", product);
        Assert.Contains("Product.Create(item.Name, item.Price, item.CategoryId, Enum.Parse<ProductStatus>(item.Status))", product);
        Assert.Contains("public sealed record ProductSeed(string Name, decimal Price, Guid CategoryId, string Status);", solution.Read("src/Shop.Persistence/Seeding/ProductSeed.cs"));
        Assert.Contains(".IgnoreQueryFilters()", product); // soft delete: silinmiş kayıt tekrar eklenmesin

        string sample = solution.Read("src/Shop.Persistence/Seeding/ProductSampleSeeder.cs");
        Assert.Contains("public bool DevelopmentOnly => true;", sample);
        Assert.Contains("public int Order => 111;", sample);
        Assert.Contains("List<Guid> categoryIds = await db.Set<Category>().Select(x => x.Id).ToListAsync(cancellationToken);", sample);
        Assert.Contains("Fit($\"{sample.ProductName()} {i + 1}\", Product.NameMaxLength), sample.Price(), sample.Pick(categoryIds), sample.Pick(Enum.GetValues<ProductStatus>())", sample);
    }

    [Fact]
    public void Seed_dialog_records_and_sample_kinds_are_written_and_read_back()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Color.cs", """
            namespace Shop.Domain.Entities;

            public enum ColorTone { Warm, Cold }

            public class Color : Entity<int>
            {
                private Color() { }

                public string Name { get; private set; } = "";
                public string? Hex { get; private set; }
                public ColorTone Tone { get; private set; }

                public static Color Create(string name, string? hex, ColorTone tone) => new() { Name = name, Hex = hex, Tone = tone };
            }
            """);
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        EntityModel color = EntityReader.Read(layout, "Color");

        SeedDescription empty = SeederGenerator.Describe(layout, color);
        Assert.Equal("none", empty.RequiredStorage);
        Assert.Equal(new[] { "Warm", "Cold" }, empty.Fields.Single(f => f.Name == "Tone").EnumValues);
        Assert.Equal("NumberedName", empty.Fields.Single(f => f.Name == "Name").SampleKind);

        // Tablodaki kayıtlar JSON'a; örnek veri seçilen türlerle.
        Dictionary<string, string?>[] records =
        [
            new() { ["Name"] = "Kırmızı", ["Hex"] = "#ff0000", ["Tone"] = "Warm" },
            new() { ["Name"] = "Mavi", ["Hex"] = "", ["Tone"] = "Cold" },
        ];
        SeederGenerator.Plan(layout, color, new SeederOptions
        {
            Records = records, SampleCount = 5, SampleKinds = new Dictionary<string, string> { ["Name"] = "City" },
        }).Apply();

        string json = solution.Read("src/Shop.Persistence/Seeding/Data/colors.json");
        Assert.Contains("{ \"name\": \"Kırmızı\", \"hex\": \"#ff0000\", \"tone\": \"Warm\" }", json);
        Assert.Contains("\"hex\": null", json);
        Assert.DoesNotContain("TODO", json);
        string sample = solution.Read("src/Shop.Persistence/Seeding/ColorSampleSeeder.cs");
        Assert.Contains("private const int Count = 5;", sample);
        Assert.Contains("Color.Create(sample.City(),", sample);

        SeedDescription described = SeederGenerator.Describe(layout, color);
        Assert.Equal("json", described.RequiredStorage);
        Assert.Equal(2, described.Records.Count);
        Assert.Equal("Mavi", described.Records[1]["Name"]);
        Assert.True(described.SampleExists);
        Assert.Equal(5, described.SampleCount);
        Assert.Equal("City", described.SampleKinds["Name"]);

        // Kayıt değişince dosya güncellenir (önizlemede fark görünür).
        SeederGenerator.Plan(layout, color, new SeederOptions { Sample = false, Records = [records[0]] }).Apply();
        Assert.DoesNotContain("Mavi", solution.Read("src/Shop.Persistence/Seeding/Data/colors.json"));

        // Migration (HasData): sabit Id'ler, kayıtlar okunabilir.
        SeederGenerator.Plan(layout, color, new SeederOptions { Sample = false, HasData = true, Records = records }).Apply();
        string seedData = solution.Read("src/Shop.Persistence/EntityConfigurations/ColorSeedData.cs");
        Assert.Contains("new { Id = 1, Name = \"Kırmızı\", Hex = \"#ff0000\", Tone = ColorTone.Warm },", seedData);
        Assert.Contains("new { Id = 2, Name = \"Mavi\", Hex = (string?)null, Tone = ColorTone.Cold },", seedData);
        SeedDescription migration = SeederGenerator.Describe(layout, color);
        Assert.Equal("migration", migration.RequiredStorage);
        Assert.Equal("2", migration.Records[1]["Id"]);
        Assert.Null(migration.Records[1]["Hex"]);
        Assert.Equal("Cold", migration.Records[1]["Tone"]);

        Assert.Throws<GeneratorException>(() => SeederGenerator.Plan(layout, color, new SeederOptions
        {
            Sample = false, Records = [new Dictionary<string, string?> { ["Name"] = "X", ["Tone"] = "" }],
        }));
    }

    [Fact]
    public void Has_data_writes_seed_data_class_and_wires_it_into_configuration()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();

        SeederGenerator.Plan(layout, EntityReader.Read(layout, "Brand"), new SeederOptions { Required = false, Sample = false, HasData = true }).Apply();

        Assert.Contains("internal static class BrandSeedData", solution.Read("src/Shop.Persistence/EntityConfigurations/BrandSeedData.cs"));
        Assert.Contains("builder.HasData(BrandSeedData.Items);", solution.Read("src/Shop.Persistence/EntityConfigurations/BrandConfiguration.cs"));
        Assert.False(solution.Exists("src/Shop.Persistence/Seeding/BrandSeeder.cs"));
    }

    [Fact]
    public void Database_is_chosen_from_the_template_comments_and_can_be_switched()
    {
        using var solution = new TestSolution();
        // Şablonun gerçek dosyaları (ad: CanAppTemplate → Shop).
        string template = TemplateFolder();
        void Copy(string from, string to) => solution.Write(to, File.ReadAllText(Path.Combine(template, from))
            .Replace("CanAppTemplate", "Shop", StringComparison.Ordinal).Replace("canapptemplate", "shop", StringComparison.Ordinal));
        Copy("Directory.Packages.props", "Directory.Packages.props");
        Copy("docker-compose.yml", "docker-compose.yml");
        Copy("src/CanAppTemplate.Persistence/CanAppTemplate.Persistence.csproj", "src/Shop.Persistence/Shop.Persistence.csproj");
        Copy("src/CanAppTemplate.Persistence/PersistenceServiceRegistration.cs", "src/Shop.Persistence/PersistenceServiceRegistration.cs");
        Copy("src/CanAppTemplate.WebAPI/appsettings.Development.json", "src/Shop.WebAPI/appsettings.Development.json");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        Assert.Equal(DatabaseProvider.None, DatabaseSelector.Current(layout));

        string packages = solution.Read("Directory.Packages.props");
        var warnings = new List<string>();
        IReadOnlyList<ChangeResult> results = DatabaseSelector.Plan(layout, DatabaseProvider.PostgreSql, warnings).Apply();
        Assert.DoesNotContain(results, r => r.Kind == ChangeKind.Failed);
        Assert.Equal(DatabaseProvider.PostgreSql, DatabaseSelector.Current(layout));
        // Paket eklenmez (sürümü kullanıcı seçer); komut verilir.
        Assert.Equal(packages, solution.Read("Directory.Packages.props"));
        Assert.Contains(warnings, w => w.Contains("dotnet add src/Shop.Persistence package Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal));
        Assert.DoesNotContain("Npgsql", solution.Read("src/Shop.Persistence/Shop.Persistence.csproj"));
        string registration = solution.Read("src/Shop.Persistence/PersistenceServiceRegistration.cs");
        Assert.DoesNotContain("Veritabanı ayarlanmadı", registration);
        Assert.Contains("// options.UseSqlServer(", registration);
        // Eksik paket koda da TODO olarak yazılır, sağlayıcı satırının hemen üstüne.
        Assert.Matches(@"// TODO\(canfxgen\): veritabanı paketi: dotnet add src/Shop\.Persistence package Npgsql\.EntityFrameworkCore\.PostgreSQL[^\n]*\n[ \t]*options\.UseNpgsql\(", registration.ReplaceLineEndings("\n"));
        IReadOnlyList<ChangeResult> again = DatabaseSelector.Plan(layout, DatabaseProvider.PostgreSql, []).Apply();
        Assert.All(again, r => Assert.NotEqual(ChangeKind.Updated, r.Kind));
        string compose = solution.Read("docker-compose.yml").ReplaceLineEndings("\n");
        Assert.Contains("\n  postgres:\n    image: postgres:", compose);
        Assert.Contains("\n  # sqlserver:", compose);
        Assert.Contains("\n  db-data:", compose);
        Assert.Contains("\n      postgres: { condition: service_healthy }", compose);
        string development = solution.Read("src/Shop.WebAPI/appsettings.Development.json");
        Assert.Contains("\n    \"Shop\": \"Host=localhost", development.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("\"Shop\": \"\"", development);
        Assert.Contains("\n      \"Database\": { \"Compose\": \"postgres\"", development.ReplaceLineEndings("\n"));

        // SQL Server'a geçiş: PostgreSQL yorumlanır.
        string csproj = solution.Read("src/Shop.Persistence/Shop.Persistence.csproj");
        solution.Write("src/Shop.Persistence/Shop.Persistence.csproj", csproj.Replace("<ItemGroup>", "<ItemGroup>\n    <PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" />", StringComparison.Ordinal));
        solution.Write("src/Shop.Persistence/Migrations/20260101000000_Initial.cs", "// eski migration");
        var switching = new List<string>();
        DatabaseSelector.Plan(layout, DatabaseProvider.SqlServer, switching).Apply();
        Assert.Equal(DatabaseProvider.SqlServer, DatabaseSelector.Current(layout));
        registration = solution.Read("src/Shop.Persistence/PersistenceServiceRegistration.cs");
        Assert.DoesNotContain("veritabanı paketi: dotnet add src/Shop.Persistence package Npgsql", registration); // eski TODO kalktı
        Assert.Contains("veritabanı paketi: dotnet add src/Shop.Persistence package Microsoft.EntityFrameworkCore.SqlServer", registration);
        Assert.Contains("veritabanı paketi: dotnet add src/Shop.Persistence package System.Security.Cryptography.Pkcs", registration);
        Assert.Contains("TODO(canfxgen): veritabanı değişti", registration);
        Assert.Contains(switching, w => w.Contains("dotnet remove src/Shop.Persistence package Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal));
        compose = solution.Read("docker-compose.yml").ReplaceLineEndings("\n");
        Assert.Contains("\n  # postgres:\n  #   image: postgres:", compose);
        Assert.Contains("\n  sqlserver:\n    image:", compose);
        Assert.Contains("\n      sqlserver: { condition: service_healthy }", compose);
        development = solution.Read("src/Shop.WebAPI/appsettings.Development.json").ReplaceLineEndings("\n");
        Assert.Contains("\n    // \"Shop\": \"Host=localhost", development);
        Assert.Contains("\n    \"Shop\": \"Server=localhost", development);

        // Elle ayarlanmış başka bir sağlayıcıya dokunulmaz.
        solution.Write("src/Shop.Persistence/PersistenceServiceRegistration.cs", "options.UseSqlite(tenantConnectionString);\n");
        Assert.Equal(DatabaseProvider.Custom, DatabaseSelector.Current(layout));
        Assert.Throws<GeneratorException>(() => DatabaseSelector.Plan(layout, DatabaseProvider.PostgreSql, []));
    }

    private static string TemplateFolder()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "templates", "CanFx.Templates", "content", "monolith");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Şablon klasörü bulunamadı.");
    }

    [Fact]
    public void Mechanism_status_shows_on_off_and_mixed_per_target()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand"), new CrudOptions { Caching = false, DynamicQuery = false }).Apply();
        MechanismEditor.Plan(layout, "GetByIdBrandQuery", [Mechanism.Caching], enable: true, []).Apply();

        MechanismReport feature = MechanismEditor.Status(layout, "Brand");
        Assert.False(feature.SingleRequest);
        MechanismStatus caching = feature.Mechanisms.Single(m => m.Mechanism == Mechanism.Caching);
        Assert.Equal(1, caching.Enabled);                     // yalnızca GetById
        Assert.True(caching.Total > 1);                       // karışık
        Assert.True(feature.Mechanisms.Single(m => m.Mechanism == Mechanism.Logging).Enabled > 0);
        Assert.Equal(0, feature.Mechanisms.Single(m => m.Mechanism == Mechanism.DynamicQuery).Enabled);

        MechanismReport single = MechanismEditor.Status(layout, "GetByIdBrandQuery");
        Assert.True(single.SingleRequest);
        Assert.Equal(new MechanismStatus(Mechanism.Caching, 1, 1), single.Mechanisms.Single(m => m.Mechanism == Mechanism.Caching));
        Assert.DoesNotContain(single.Mechanisms, m => m.Mechanism == Mechanism.DynamicQuery);
    }

    [Fact]
    public void Mechanisms_can_be_toggled_later_with_preview()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand"), new CrudOptions { Caching = false }).Apply();

        string getById = Feature + "Queries/GetById/GetByIdBrandQuery.cs";
        string before = solution.Read(getById);
        Assert.DoesNotContain("ICachableRequest", before);

        // Önizleme: dosya değişmez, farkı görünür.
        var warnings = new List<string>();
        GenerationPlan plan = MechanismEditor.Plan(layout, "Brand", [Mechanism.Caching], enable: true, warnings);
        IReadOnlyList<FilePreview> preview = plan.Preview();
        Assert.Contains(preview, p => p.Path.EndsWith("GetByIdBrandQuery.cs", StringComparison.Ordinal) && p.After!.Contains("ICachableRequest", StringComparison.Ordinal));
        Assert.Equal(before, solution.Read(getById));
        Assert.Contains(warnings, w => w.Contains("GetListByDynamicBrandQuery", StringComparison.Ordinal)); // dinamik sorgu önbelleğe alınmaz

        // Entity için aç: tüm sorgular önbellekli, anahtar parametrelerden.
        plan.Apply();
        string cached = solution.Read(getById);
        Assert.Contains("ISecuredRequest, ICachableRequest", cached);
        Assert.Contains("public string CacheKey => $\"GetByIdBrand({Id})\";", cached);
        Assert.Contains("[CanFx.Application.CacheTags.Entity<Brand>()]", cached);
        Assert.Contains("public string CacheKey => $\"GetListBrand({PageRequest.Page},{PageRequest.PageSize})\";", solution.Read(Feature + "Queries/GetList/GetListBrandQuery.cs"));
        Assert.Contains("ICacheRemoverRequest", solution.Read(Feature + "Commands/Create/CreateBrandCommand.cs"));

        // Tek istek için kapat; tekrar çalıştırmak bir şey değiştirmez.
        MechanismEditor.Plan(layout, "GetByIdBrandQuery", [Mechanism.Caching, Mechanism.Secured], enable: false, []).Apply();
        string plain = solution.Read(getById);
        Assert.DoesNotContain("ICachableRequest", plain);
        Assert.DoesNotContain("CacheKey", plain);
        Assert.DoesNotContain("ISecuredRequest", plain);
        Assert.DoesNotContain("Permissions", plain);
        Assert.Contains("public sealed class GetByIdBrandQuery : IRequest<GetByIdBrandResponse>\n", plain.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("\n\n\n", plain.ReplaceLineEndings("\n"));
        IReadOnlyList<ChangeResult> again = MechanismEditor.Plan(layout, "GetByIdBrandQuery", [Mechanism.Caching], enable: false, []).Apply();
        Assert.All(again, r => Assert.Equal(ChangeKind.Unchanged, r.Kind));

        // Yetkiyi geri aç: Admin + Read.
        MechanismEditor.Plan(layout, "GetByIdBrandQuery", [Mechanism.Secured], enable: true, []).Apply();
        Assert.Contains("public IReadOnlyCollection<string> Permissions => [BrandsOperationClaims.Admin, BrandsOperationClaims.Read];", solution.Read(getById));

        // Transaction kapatılınca handler kendisi kaydeder: IUnitOfWork enjekte edilir, return'den önce SaveChangesAsync.
        string updateHandler = Feature + "Commands/Update/UpdateBrandCommandHandler.cs";
        Assert.DoesNotContain("SaveChangesAsync", solution.Read(updateHandler));
        MechanismEditor.Plan(layout, "UpdateBrandCommand", [Mechanism.Transaction], enable: false, []).Apply();
        Assert.DoesNotContain("ITransactionalRequest", solution.Read(Feature + "Commands/Update/UpdateBrandCommand.cs"));
        string saving = solution.Read(updateHandler).ReplaceLineEndings("\n");
        Assert.Contains("private readonly IUnitOfWork _unitOfWork;", saving);
        Assert.Contains("IUnitOfWork unitOfWork)", saving);
        Assert.Contains("_unitOfWork = unitOfWork;", saving);
        Assert.Contains("using CanFx.Persistence.Repositories;", saving);
        Assert.Matches(@"await _unitOfWork\.SaveChangesAsync\(cancellationToken\);[^\n]*\n\s*return ", saving);
        IReadOnlyList<ChangeResult> saveAgain = MechanismEditor.Plan(layout, "UpdateBrandCommand", [Mechanism.Transaction], enable: false, []).Apply();
        Assert.All(saveAgain, r => Assert.Equal(ChangeKind.Unchanged, r.Kind));

        // Geri açılınca eklenen kayıt satırı kalkar.
        MechanismEditor.Plan(layout, "UpdateBrandCommand", [Mechanism.Transaction], enable: true, []).Apply();
        Assert.Contains("ITransactionalRequest", solution.Read(Feature + "Commands/Update/UpdateBrandCommand.cs"));
        Assert.DoesNotContain("SaveChangesAsync", solution.Read(updateHandler));

        // Transaction ve log sorgulara eklenmez; durum yalnızca komutları sayar.
        MechanismEditor.Plan(layout, "Brand", [Mechanism.Transaction, Mechanism.Logging], enable: true, []).Apply();
        Assert.DoesNotContain("ITransactionalRequest", solution.Read(getById));
        Assert.DoesNotContain("ILoggableRequest", solution.Read(getById));
        MechanismReport status = MechanismEditor.Status(layout, "Brand");
        MechanismStatus transaction = status.Mechanisms.Single(m => m.Mechanism == Mechanism.Transaction);
        Assert.Equal(transaction.Total, transaction.Enabled);
        Assert.True(transaction.Total < status.Requests);

        // Dinamik sorguyu kapat: dosyalar, eşleme ve uçlar gider.
        IReadOnlyList<ChangeResult> dynamic = MechanismEditor.Plan(layout, "Brand", [Mechanism.DynamicQuery], enable: false, []).Apply();
        Assert.Contains(dynamic, r => r.Kind == ChangeKind.Deleted);
        Assert.False(solution.Exists(Feature + "Queries/GetListByDynamic/GetListByDynamicBrandQuery.cs"));
        Assert.DoesNotContain("GetListByDynamic", solution.Read(Feature + "Profiles/MappingProfiles.cs"));
        Assert.DoesNotContain("GetListByDynamic", solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs"));

        // ve geri aç
        MechanismEditor.Plan(layout, "Brand", [Mechanism.DynamicQuery], enable: true, []).Apply();
        Assert.True(solution.Exists(Feature + "Queries/GetListByDynamic/GetListByDynamicBrandQuery.cs"));
        Assert.Contains("GetListByDynamic", solution.Read("src/Shop.WebAPI/Controllers/BrandsController.cs"));
    }

    [Fact]
    public void Disabling_transaction_saves_in_handlers_it_cannot_rewrite_with_a_todo()
    {
        using var solution = new TestSolution();
        const string Folder = "src/Shop.Application/Features/Orders/Commands/Ship/";
        solution.Write(Folder + "ShipOrderCommand.cs", """
            namespace Shop.Application.Features.Orders.Commands.Ship;

            public sealed class ShipOrderCommand : IRequest<bool>, ITransactionalRequest
            {
                public Guid Id { get; init; }
            }
            """);
        solution.Write(Folder + "ShipOrderCommandHandler.cs", """
            namespace Shop.Application.Features.Orders.Commands.Ship;

            public sealed class ShipOrderCommandHandler(IOrderRepository orders) : IRequestHandler<ShipOrderCommand, bool>
            {
                /// <summary>Siparişi kargolar.</summary>
                public async Task<bool> Handle(ShipOrderCommand request, CancellationToken cancellationToken)
                {
                    Order? order = await orders.GetAsync(request.Id, cancellationToken);
                    if (order is null)
                        return false;

                    order.Ship();
                    return true;
                }
            }
            """);
        SolutionLayout layout = SolutionLayout.Find(solution.Root);

        MechanismEditor.Plan(layout, "ShipOrderCommand", [Mechanism.Transaction], enable: false, []).Apply();
        string handler = solution.Read(Folder + "ShipOrderCommandHandler.cs").ReplaceLineEndings("\n");
        Assert.Contains("(IOrderRepository orders, IUnitOfWork unitOfWork)", handler);
        Assert.Contains("await unitOfWork.SaveChangesAsync(cancellationToken); // transaction kapalı: handler kendisi kaydeder\n        return true;", handler);
        // Tek satırlık if içindeki return'e yerleştirilemez: TODO, metodun yorumunun üstünde.
        Assert.Contains("    // TODO(canfxgen): transaction kapalı", handler);
        Assert.True(handler.IndexOf("TODO(canfxgen)", StringComparison.Ordinal) < handler.IndexOf("/// <summary>Siparişi", StringComparison.Ordinal));

        // Senkron Handle: yalnızca TODO.
        solution.Write(Folder + "ShipOrderCommandHandler.cs", """
            namespace Shop.Application.Features.Orders.Commands.Ship;

            public sealed class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, bool>
            {
                public Task<bool> Handle(ShipOrderCommand request, CancellationToken cancellationToken) => Task.FromResult(true);
            }
            """);
        MechanismEditor.Plan(layout, "ShipOrderCommand", [Mechanism.Transaction], enable: false, []).Apply();
        string sync = solution.Read(Folder + "ShipOrderCommandHandler.cs");
        Assert.Contains("TODO(canfxgen): transaction kapalı", sync);
        Assert.DoesNotContain("IUnitOfWork unitOfWork", sync);
    }

    [Fact]
    public void Rate_limit_policy_is_bound_to_endpoints_and_added_to_settings()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Domain/Entities/Brand.cs", "namespace Shop.Domain.Entities; public class Brand : Entity<Guid> { public string Name { get; set; } = \"\"; }");
        solution.Write("src/Shop.WebAPI/appsettings.json", """
            {
              "RateLimiting": {
                "Enabled": true,
                // Uygulamanın kendi sınırları
                "Policies": {
                }
              }
            }
            """);
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        CrudGenerator.Plan(layout, EntityReader.Read(layout, "Brand")).Apply();
        string controller = "src/Shop.WebAPI/Controllers/BrandsController.cs";

        // İsteğe bağla: onu gönderen action'a attribute, appsettings'e politika.
        RateLimitBinder.Plan(layout, "reports", "CreateBrandCommand", new RateLimitOptions { PermitLimit = 20, PartitionBy = "Tenant" }, []).Apply();
        string code = solution.Read(controller).ReplaceLineEndings("\n");
        Assert.Contains("    [HttpPost]\n    [EnableRateLimiting(\"reports\")]\n    public async Task<IActionResult> Add(", code);
        Assert.Contains("using Microsoft.AspNetCore.RateLimiting;", code);
        Assert.Single(code.Split("EnableRateLimiting(")[1..]);

        string settings = solution.Read("src/Shop.WebAPI/appsettings.json");
        Assert.Contains("\"reports\": { \"PermitLimit\": 20, \"Window\": \"00:01:00\", \"SegmentsPerWindow\": 1, \"PartitionBy\": \"Tenant\" }", settings);
        using (System.Text.Json.JsonDocument.Parse(settings, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })) { }

        // İkinci politika: virgülle eklenir, JSON geçerli kalır; aynı uca başka politika verilince değiştirilir.
        RateLimitBinder.Plan(layout, "heavy", "BrandsController.Update", new RateLimitOptions(), []).Apply();
        RateLimitBinder.Plan(layout, "heavy", "CreateBrandCommand", new RateLimitOptions(), []).Apply();
        code = solution.Read(controller);
        Assert.DoesNotContain("EnableRateLimiting(\"reports\")", code);
        Assert.Equal(2, code.Split("EnableRateLimiting(\"heavy\")").Length - 1);
        settings = solution.Read("src/Shop.WebAPI/appsettings.json");
        Assert.Contains("\"heavy\"", settings);
        using (System.Text.Json.JsonDocument.Parse(settings, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })) { }

        // Hazır politika sabitle; controller'ın tamamına; kaldırma.
        RateLimitBinder.Plan(layout, "sensitive", "Brand", new RateLimitOptions(), []).Apply();
        code = solution.Read(controller);
        Assert.Contains("[ApiController]\n[EnableRateLimiting(CanRateLimitPolicies.Sensitive)]\npublic class BrandsController", code.ReplaceLineEndings("\n"));
        Assert.Contains("using CanFx.WebApi.RateLimiting;", code);

        RateLimitBinder.Plan(layout, "heavy", "CreateBrandCommand", new RateLimitOptions { Remove = true }, []).Apply();
        Assert.Single(solution.Read(controller).Split("EnableRateLimiting(\"heavy\")")[1..]);

        Assert.Throws<GeneratorException>(() => RateLimitBinder.Plan(layout, "Bad Name", "CreateBrandCommand", new RateLimitOptions(), []));
    }

    [Fact]
    public void Feature_difference_of_the_template_is_patched_into_the_project()
    {
        using var solution = new TestSolution();
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        string temp = Path.Combine(Path.GetTempPath(), "canfxgen-test-" + Guid.NewGuid().ToString("N"));
        string baseDir = Path.Combine(temp, "base"), targetDir = Path.Combine(temp, "target");

        void Both(string relative, string baseText, string targetText)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(baseDir, relative))!);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(targetDir, relative))!);
            File.WriteAllText(Path.Combine(baseDir, relative), baseText);
            File.WriteAllText(Path.Combine(targetDir, relative), targetText);
        }

        try
        {
            // Şablon: Program.cs'e iki satır, yeni kayıt dosyası, appsettings'e bölüm.
            Both("src/Shop.WebAPI/Program.cs",
                "var builder = WebApplication.CreateBuilder(args);\nbuilder.Services.AddA();\nbuilder.Services.AddB();\nvar app = builder.Build();\napp.Run();\n",
                "var builder = WebApplication.CreateBuilder(args);\nbuilder.Services.AddA();\nbuilder.Services.AddRedis();\nbuilder.Services.AddB();\nvar app = builder.Build();\napp.UseRedis();\napp.Run();\n");
            Directory.CreateDirectory(Path.Combine(targetDir, "src/Shop.Infrastructure/Caching"));
            File.WriteAllText(Path.Combine(targetDir, "src/Shop.Infrastructure/Caching/RedisServiceRegistration.cs"), "// redis\n");
            Both("canfx.json", "{ \"options\": { \"Redis\": false } }", "{ \"options\": { \"Redis\": true } }");

            // Proje: kullanıcı satır eklemiş, girinti farklı.
            solution.Write("src/Shop.WebAPI/Program.cs",
                "var builder = WebApplication.CreateBuilder(args);\n// benim ayarım\nbuilder.Services.AddA();\nbuilder.Services.AddB();\nbuilder.Services.AddMine();\nvar app = builder.Build();\n  app.MapMine();\napp.Run();\n");
            solution.Write("canfx.json", "{\n  // yorum\n  \"options\": { \"Redis\": false }\n}\n");

            var warnings = new List<string>();
            IReadOnlyList<ChangeResult> results = FeaturePatcher.Plan(layout, baseDir, targetDir, "Redis", enable: true, warnings).Apply();
            Assert.DoesNotContain(results, r => r.Kind == ChangeKind.Failed);
            Assert.Empty(warnings);

            string program = solution.Read("src/Shop.WebAPI/Program.cs");
            Assert.Contains("builder.Services.AddA();\nbuilder.Services.AddRedis();\nbuilder.Services.AddB();\nbuilder.Services.AddMine();", program);
            Assert.Contains("app.UseRedis();", program);
            Assert.Contains("// benim ayarım", program);
            Assert.True(solution.Exists("src/Shop.Infrastructure/Caching/RedisServiceRegistration.cs"));
            Assert.Contains("\"Redis\": true", solution.Read("canfx.json"));
            Assert.Contains("// yorum", solution.Read("canfx.json"));

            // Tekrar uygulamak bir şey değiştirmez; geri almak (hedef → taban) eski haline döndürür.
            FeaturePatcher.Plan(layout, baseDir, targetDir, "Redis", enable: true, []).Apply();
            Assert.Single(solution.Read("src/Shop.WebAPI/Program.cs").Split("AddRedis")[1..]);

            FeaturePatcher.Plan(layout, targetDir, baseDir, "Redis", enable: false, []).Apply();
            string removed = solution.Read("src/Shop.WebAPI/Program.cs");
            Assert.DoesNotContain("Redis", removed);
            Assert.Contains("builder.Services.AddMine();", removed);
            Assert.False(solution.Exists("src/Shop.Infrastructure/Caching/RedisServiceRegistration.cs"));

            // Yerleştirilemeyen değişiklik elle yapılmak üzere raporlanır.
            solution.Write("src/Shop.WebAPI/Program.cs", "// tamamen farklı\n");
            var manual = new List<string>();
            FeaturePatcher.Plan(layout, baseDir, targetDir, "Redis", enable: true, manual).Apply();
            Assert.Contains(manual, w => w.Contains("AddRedis", StringComparison.Ordinal));
            Assert.StartsWith("// TODO(canfxgen): Redis eklenirken ", solution.Read("src/Shop.WebAPI/Program.cs"));

            // Değiştirilmiş dosya özellik kapanınca silinmez; başına TODO yazılır.
            solution.Write("src/Shop.Infrastructure/Caching/RedisServiceRegistration.cs", "// redis\n// benim eklediğim\n");
            var kept = new List<string>();
            FeaturePatcher.Plan(layout, targetDir, baseDir, "Redis", enable: false, kept).Apply();
            Assert.Contains(kept, w => w.Contains("silinmedi", StringComparison.Ordinal));
            Assert.StartsWith("// TODO(canfxgen): Redis kapatıldı; bu dosya değiştirilmiş", solution.Read("src/Shop.Infrastructure/Caching/RedisServiceRegistration.cs"));

            Assert.Equal("<?xml version=\"1.0\"?>\n<!-- TODO(canfxgen): x -->\n<Project />", FeaturePatcher.AddHeaderTodo("<?xml version=\"1.0\"?>\n<Project />", "a.csproj", "TODO(canfxgen): x"));
            Assert.Equal("{}", FeaturePatcher.AddHeaderTodo("{}", "a.json", "TODO(canfxgen): x"));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Schema_feature_leaves_a_migration_todo_when_the_project_uses_migrations()
    {
        using var solution = new TestSolution();
        solution.Write("src/Shop.Persistence/Contexts/BaseDbContext.cs", "namespace Shop.Persistence.Contexts;\n");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        string empty = Path.Combine(Path.GetTempPath(), "canfxgen-test-" + Guid.NewGuid().ToString("N"));

        // Migration yoksa (StartupMode ile kuruluyor) TODO gerekmez.
        FeaturePatcher.Plan(layout, empty, empty, "AuditTrail", enable: true, []).Apply();
        Assert.DoesNotContain("TODO", solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs"));

        solution.Write("src/Shop.Persistence/Migrations/20260101000000_Initial.cs", "// migration");
        FeaturePatcher.Plan(layout, empty, empty, "AuditTrail", enable: true, []).Apply();
        FeaturePatcher.Plan(layout, empty, empty, "AuditTrail", enable: true, []).Apply();
        string context = solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs");
        Assert.StartsWith("// TODO(canfxgen): AuditTrail tablo ekledi: yeni bir migration ekle (canfxgen migration add AddAuditTrail)", context);
        Assert.Single(context.Split("TODO(canfxgen)")[1..]);

        // Redis şema değiştirmez.
        FeaturePatcher.Plan(layout, empty, empty, "Redis", enable: true, []).Apply();
        Assert.Single(solution.Read("src/Shop.Persistence/Contexts/BaseDbContext.cs").Split("TODO(canfxgen)")[1..]);
    }

    [Fact]
    public void Feature_names_are_normalized_and_options_inferred_for_old_projects()
    {
        Assert.Equal("EventBus", FeaturePatcher.Normalize("event-bus"));
        Assert.Equal("Search", FeaturePatcher.Normalize("elasticsearch"));
        Assert.Equal("Redis", FeaturePatcher.Normalize("REDIS"));
        Assert.Throws<GeneratorException>(() => FeaturePatcher.Normalize("kafka"));

        using var solution = new TestSolution();
        solution.Write("Directory.Packages.props", "<PackageVersion Include=\"CanFx.Caching.Redis\" />");
        SolutionLayout layout = SolutionLayout.Find(solution.Root);
        var warnings = new List<string>();
        Dictionary<string, string> options = FeaturePatcher.ReadOptions(layout, warnings);
        Assert.Equal("true", options["Redis"]);
        Assert.Equal("false", options["Search"]);
        Assert.Single(warnings);
    }

    [Fact]
    public void Feature_patch_ignores_whitespace_differences_and_removed_options()
    {
        // Rider'ın yeniden biçimlendirdiği satır ("/>" bitişik, farklı girinti) zaten eklenmiş sayılmalı (NU1008).
        string[] baseLines = ["<ItemGroup>", "  <PackageReference Include=\"CanFx.Core\" />", "</ItemGroup>"];
        string[] targetLines = ["<ItemGroup>", "  <PackageReference Include=\"CanFx.Core\" />", "  <PackageReference Include=\"CanFx.EventBus\" />", "</ItemGroup>"];
        string project = "<ItemGroup>\n        <PackageReference Include=\"CanFx.Core\"/>\n        <PackageReference Include=\"CanFx.EventBus\"/>\n</ItemGroup>\n";
        (string text, List<string> failed) = FeaturePatcher.ApplyHunks(project, FeaturePatcher.Hunks(baseLines, targetLines), baseLines.Length);
        Assert.Empty(failed);
        Assert.Single(text.Split("CanFx.EventBus")[1..]);

        // Şablondan kaldırılan seçenek (eski canfx.json'daki "Database") şablona verilmez.
        using var solution = new TestSolution();
        solution.Write("canfx.json", "{ \"options\": { \"Database\": \"postgresql\", \"Redis\": true } }");
        Dictionary<string, string> options = FeaturePatcher.ReadOptions(SolutionLayout.Find(solution.Root), []);
        Assert.False(options.ContainsKey("Database"));
        Assert.Equal("true", options["Redis"]);
    }

    /// <summary>İstek ve handler'ı birlikte okur (handler <c>{İstek}Handler.cs</c>'te).</summary>
    private static string ReadWithHandler(TestSolution solution, string requestPath) =>
        solution.Read(requestPath) + "\n" + solution.Read(requestPath[..^3] + "Handler.cs");
}
