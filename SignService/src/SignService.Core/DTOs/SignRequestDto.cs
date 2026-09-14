using System.ComponentModel.DataAnnotations;

namespace SignService.Core.DTOs;

public class SignRequestDto
{
    [Required]
    public Guid DocId { get; set; }

    [Required]
    public Guid SignerId { get; set; }

    public string? SignerName { get; set; } // Tên hiển thị trên chữ ký

    public string? Reason { get; set; } // Lý do ký
}
