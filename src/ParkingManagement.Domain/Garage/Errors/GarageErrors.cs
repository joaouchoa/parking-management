namespace ParkingManagement.Domain.Garage.Errors;

public static class GarageErrors
{
    public const string CodigoSetorObrigatorio = "O código do setor é obrigatório.";
    public const string BasePriceInvalido = "O preço base do setor deve ser maior que zero.";
    public const string CapacidadeMaximaInvalida = "A capacidade máxima do setor deve ser maior que zero.";
    public const string VagaJaOcupada = "A vaga informada já está ocupada.";
    public const string VagaJaLivre = "A vaga informada já está livre.";
}
