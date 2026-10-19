using System.Reflection;
using MediatR;
using NetArchTest.Rules;
using OrderService.Application.Orders.Commands.PlaceOrder;
using OrderService.Domain.Abstractions;
using OrderService.Infrastructure.Write;

namespace OrderService.UnitTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(PlaceOrderCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(OrderDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private static Types GetTypes(Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
        {
            return Types.InAssembly(assembly);
        }

        var fileName = assembly.GetName().Name + ".dll";
        var binDir = AppDomain.CurrentDomain.GetData("APP_BIN_DIR") as string;
        if (!string.IsNullOrEmpty(binDir))
        {
            var p = Path.Combine(binDir, fileName);
            if (File.Exists(p)) return Types.FromFile(p);
        }

        var current = Directory.GetCurrentDirectory();
        while (current is not null)
        {
            var searchPaths = Directory.GetFiles(current, fileName, SearchOption.AllDirectories);
            if (searchPaths.Length > 0)
            {
                return Types.FromFile(searchPaths[0]);
            }
            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return Types.InAssembly(assembly);
    }

    [Fact]
    public void Domain_ShouldNotDependOn_ApplicationInfrastructureOrApi()
    {
        var result = GetTypes(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationAssembly.GetName().Name!,
                InfrastructureAssembly.GetName().Name!,
                ApiAssembly.GetName().Name!,
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Application_ShouldNotDependOn_InfrastructureOrApi()
    {
        var result = GetTypes(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                InfrastructureAssembly.GetName().Name!,
                ApiAssembly.GetName().Name!,
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Api()
    {
        var result = GetTypes(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiAssembly.GetName().Name!)
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Handlers_InApplication_ShouldBeSealed()
    {
        var handlerTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                t.GetInterfaces().Any(i =>
                    (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)) ||
                    (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))))
            .ToList();

        Assert.NotEmpty(handlerTypes);

        foreach (var handler in handlerTypes)
        {
            Assert.True(handler.IsSealed, $"Handler {handler.FullName} should be sealed.");
        }
    }

    [Fact]
    public void DomainEntities_ShouldNotHavePublicPropertySetters()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Entity)));

        foreach (var type in entityTypes)
        {
            var publicSetters = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is not null && p.SetMethod.IsPublic)
                .ToList();

            Assert.Empty(publicSetters);
        }
    }
}
