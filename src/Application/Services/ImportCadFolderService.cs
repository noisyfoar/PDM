using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;

namespace Application.Services;

public sealed class ImportCadFolderService : IImportCadFolderService
{
    private readonly ICadFolderDocumentReader _reader;
    private readonly ICadDomainMapper _mapper;
    private readonly IUnitOfWork _unitOfWork;

    public ImportCadFolderService(ICadFolderDocumentReader reader, ICadDomainMapper mapper, IUnitOfWork unitOfWork)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public Task<ImportCadFolderReport> ExecuteAsync(string folderPath, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
