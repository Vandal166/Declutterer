namespace Declutterer.Domain.Models;

public sealed record SelectionUpdateRequest(TreeNode Node, bool IsCheckboxSelected);