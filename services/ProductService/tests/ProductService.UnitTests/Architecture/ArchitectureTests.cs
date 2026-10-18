using System.Reflection;
using NetArchTest.Rules;
using ProductService.Application.Abstractions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;
using ProductService.Infrastructure.Persistence;

namespace ProductService.UnitTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ICommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ProductDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOn_Application_Infrastructure_Api_Or_EfCore()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ProductService.Application",
                "ProductService.Infrastructure",
                "ProductService.Api",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure_Api_Or_EfCore()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ProductService.Infrastructure",
                "ProductService.Api",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void CommandHandlers_ShouldNotUse_IProductReadService()
    {
        var netArchResult = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .ShouldNot()
            .HaveDependencyOn(typeof(IProductReadService).FullName!)
            .GetResult();

        Assert.True(netArchResult.IsSuccessful);

        // Additional reflection check on constructor parameters and fields of every command handler.
        var commandHandlerTypes = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetInterfaces().Any(i =>
                            i.IsGenericType &&
                            (i.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ||
                             i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))))
            .ToList();

        Assert.NotEmpty(commandHandlerTypes);

        foreach (var handlerType in commandHandlerTypes)
        {
            var ctorParamTypes = handlerType.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType);

            Assert.DoesNotContain(typeof(IProductReadService), ctorParamTypes);
        }
    }

    [Fact]
    public void QueryHandlers_ShouldNotUse_IProductRepository_Or_IUnitOfWork()
    {
        var netArchResult = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .ShouldNot()
            .HaveDependencyOnAny(
                typeof(IProductRepository).FullName!,
                typeof(IUnitOfWork).FullName!)
            .GetResult();

        Assert.True(netArchResult.IsSuccessful);

        // Additional reflection check on constructor parameters of every query handler.
        var queryHandlerTypes = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetInterfaces().Any(i =>
                            i.IsGenericType &&
                            i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .ToList();

        Assert.NotEmpty(queryHandlerTypes);

        foreach (var handlerType in queryHandlerTypes)
        {
            var ctorParamTypes = handlerType.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .ToList();

            Assert.DoesNotContain(typeof(IProductRepository), ctorParamTypes);
            Assert.DoesNotContain(typeof(IUnitOfWork), ctorParamTypes);
        }
    }
}
