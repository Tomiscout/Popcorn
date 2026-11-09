using System.Data;
using Dapper;

namespace Popcorn.Persistence.Dapper;

public class SemiColonTypeHandler : SqlMapper.TypeHandler<List<string>?>
{
    public override void SetValue(IDbDataParameter parameter, List<string>? value)
    {
        parameter.DbType    = DbType.String;                  // <- tells ADO it’s a string
        parameter.Value     = value == null 
            ? (object)DBNull.Value 
            : string.Join(";", value);
    }

    public override List<string>? Parse(object value)
    {
        if (value == null || value == DBNull.Value) 
            return new List<string>();

        return value
            .ToString()
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }
}