namespace DocumentService.Core.DTOs;

public sealed record DocumentFileDto(Stream Content, string FileName);
