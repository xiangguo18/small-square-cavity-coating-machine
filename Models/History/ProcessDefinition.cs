namespace Small_square_cavity_coating_machine.Models.History;

public sealed record ProcessDefinition(int Id, string Name, string Group, string DataType,
    string Address, string Unit, double Min, double Max, string Description);
