using FinViet.Application.Common;
using FinViet.Application.DTOs.Categories;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryResponse>>>> GetCategories(
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetCategoriesAsync(type, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<CategoryResponse>>.Ok(
            categories,
            "Categories retrieved successfully"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> GetCategoryById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.GetCategoryByIdAsync(id, cancellationToken);

        if (category is null)
            return NotFound(ApiResponse<CategoryResponse>.Fail("Category not found."));

        return Ok(ApiResponse<CategoryResponse>.Ok(
            category,
            "Category retrieved successfully"));
    }

    [HttpPost]
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

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> UpdateCategory(
        [FromRoute] Guid id,
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

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteCategory(
        [FromRoute] Guid id,
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
