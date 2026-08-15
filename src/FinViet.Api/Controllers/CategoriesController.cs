using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Categories;
using FinViet.Application.Features.Categories.Commands.UploadCategoryIcon;
using FinViet.Application.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly IMediator _mediator;

    public CategoriesController(ICategoryService categoryService, IMediator mediator)
    {
        _categoryService = categoryService;
        _mediator = mediator;
    }

    [HttpPost("icons")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<string>>> UploadCategoryIcon(
        IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<string>.Fail("No file provided."));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);

        var url = await _mediator.Send(
            new UploadCategoryIconCommand(ms.ToArray(), file.FileName, file.ContentType),
            cancellationToken);

        return Ok(ApiResponse<string>.Ok(url, "Icon uploaded successfully"));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryResponse>>>> GetCategories(
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetCategoriesAsync(type, CurrentCustomerIdOrNull(), cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<CategoryResponse>>.Ok(
            categories,
            "Categories retrieved successfully"));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> GetCategoryById(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.GetCategoryByIdAsync(id, CurrentCustomerIdOrNull(), cancellationToken);

        if (category is null)
            return NotFound(ApiResponse<CategoryResponse>.Fail("Category not found."));

        return Ok(ApiResponse<CategoryResponse>.Ok(
            category,
            "Category retrieved successfully"));
    }

    // ── Customer bucket self-service (no admin approval needed) ─────────

    [HttpPut("{id}/bucket")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> SetBucket(
        [FromRoute] string id,
        [FromBody] SetCategoryBucketRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var category = await _categoryService.SetCustomerBucketAsync(customerId, id, request.BucketId, cancellationToken);

        return Ok(ApiResponse<CategoryResponse>.Ok(
            category,
            "Category bucket updated successfully"));
    }

    [HttpDelete("{id}/bucket")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> ResetBucket(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var category = await _categoryService.ResetCustomerBucketAsync(customerId, id, cancellationToken);

        return Ok(ApiResponse<CategoryResponse>.Ok(
            category,
            "Category bucket reset to default successfully"));
    }

    private Guid? CurrentCustomerIdOrNull()
        => User.IsInRole("Customer") ? User.GetCustomerId() : null;

    [HttpPost("custom")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> CreateCustomCategory(
        [FromBody] CreateCustomCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var category = await _categoryService.CreateCustomCategoryAsync(customerId, request, cancellationToken);

        return CreatedAtAction(
            nameof(GetCategoryById),
            new { id = category.CategoryId },
            ApiResponse<CategoryResponse>.Ok(
                category,
                "Category created successfully"));
    }

    [HttpDelete("custom/{id}")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteCustomCategory(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var deleted = await _categoryService.DeleteCustomCategoryAsync(customerId, id, cancellationToken);

        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Category not found."));

        return Ok(ApiResponse<object?>.Ok(
            null,
            "Category deleted successfully"));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> CreateCategory(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.CreateCategoryAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetCategoryById),
            new { id = category.CategoryId },
            ApiResponse<CategoryResponse>.Ok(
                category,
                "Category created successfully"));
    }

    [HttpPatch("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> UpdateCategory(
        [FromRoute] string id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.UpdateCategoryAsync(id, request, cancellationToken);

        if (category is null)
            return NotFound(ApiResponse<CategoryResponse>.Fail("Category not found."));

        return Ok(ApiResponse<CategoryResponse>.Ok(
            category,
            "Category updated successfully"));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteCategory(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var deleted = await _categoryService.DeleteCategoryAsync(id, cancellationToken);

        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Category not found."));

        return Ok(ApiResponse<object?>.Ok(
            null,
            "Category deleted successfully"));
    }
}
