using Contracts.CAD.Enums;
using Domain.Exceptions;
using Domain.Objects;

namespace Application.Services;

public static class ObjectTypeMapper
{
    public static ObjectType ToDomain(this CadDocumentType type) => type switch
    {
        CadDocumentType.Assembly => ObjectType.Assembly,
        CadDocumentType.Part => ObjectType.Part,
        CadDocumentType.StandardPart => ObjectType.StandardPart,
        _ => throw new PDMException($"Неизвестный тип CAD-документа: {type}.")
    };
    public static ObjectType ToDomain(this string type) => type switch
    {
        "Assembly" => ObjectType.Assembly,
        "Part" => ObjectType.Part,
        "StandardPart" => ObjectType.StandardPart,
        _ => throw new PDMException($"Неизвестный тип CAD-документа: {type}.")
    };
    public static CadDocumentType ToDocument (this ObjectType type) => type switch
    {
        ObjectType.Assembly => CadDocumentType.Assembly,
        ObjectType.Part => CadDocumentType.Part,
        ObjectType.StandardPart => CadDocumentType.StandardPart,
        _ => throw new PDMException($"Неизвестный тип CAD-документа: {type}.")
    };
    public static CadDocumentType ToDocument(this string type) => type switch
    {
        "Assembly" => CadDocumentType.Assembly,
        "Part" => CadDocumentType.Part,
        "StandardPart" => CadDocumentType.StandardPart,
        _ => throw new PDMException($"Неизвестный тип CAD-документа: {type}.")
    };
}
