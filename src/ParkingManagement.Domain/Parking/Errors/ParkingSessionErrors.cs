namespace ParkingManagement.Domain.Parking.Errors;

public static class ParkingSessionErrors
{
    public const string PlacaObrigatoria = "A placa do veículo é obrigatória.";
    public const string PlacaFormatoInvalido = "A placa do veículo possui formato inválido.";
    public const string GaragemCheia = "Não é possível registrar entrada: garagem no limite de capacidade.";
    public const string OcupacaoPercentualInvalida = "O percentual de ocupação informado é inválido.";
    public const string SessaoJaEstacionada = "Esta sessão já foi registrada como estacionada.";
    public const string SessaoJaFinalizada = "Esta sessão já foi finalizada.";
    public const string SessaoNaoEstacionada = "Não é possível registrar a saída de uma sessão que ainda não foi estacionada.";
    public const string BasePriceInvalido = "O preço base do setor deve ser maior que zero.";
    public const string ExitTimeAnteriorAEntrada = "O horário de saída não pode ser anterior ao horário de entrada.";
}
