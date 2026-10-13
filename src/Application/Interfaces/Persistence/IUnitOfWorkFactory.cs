namespace Application.Interfaces.Persistence;

public interface IUnitOfWorkFactory
{
    IUnitOfWork CreateUnitOfWork();
}
