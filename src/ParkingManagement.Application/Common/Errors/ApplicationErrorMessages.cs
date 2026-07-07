namespace ParkingManagement.Application.Common.Errors;

public static class ApplicationErrorMessages
{
    public static class Parking
    {
        public const string LicensePlateObrigatoria = "A placa do veículo é obrigatória.";
        public const string EntryTimeObrigatorio = "O horário de entrada é obrigatório.";
        public const string ExitTimeObrigatorio = "O horário de saída é obrigatório.";
        public const string SessaoAtivaNaoEncontrada = "Nenhuma sessão ativa encontrada para esta placa.";
        public const string VagaNaoEncontradaPorCoordenada = "Nenhuma vaga corresponde às coordenadas informadas.";
        public const string SetorDaSessaoNaoEncontrado = "Setor associado à sessão não foi encontrado.";
    }

    public static class Garage
    {
        public const string SetorNaoEncontrado = "Setor não encontrado.";
        public const string FalhaSincronizacao = "Falha ao sincronizar a configuração da garagem com o simulador.";
        public const string ListaSetoresObrigatoria = "A lista de setores (garage) é obrigatória.";
        public const string ListaVagasObrigatoria = "A lista de vagas (spots) é obrigatória.";
    }

    public static class Revenue
    {
        public const string SetorObrigatorio = "O setor é obrigatório.";
        public const string DataObrigatoria = "A data é obrigatória.";
    }
}
