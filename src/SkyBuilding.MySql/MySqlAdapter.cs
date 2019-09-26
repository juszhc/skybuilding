using MySql.Data.MySqlClient;
using SkyBuilding.ORM;
using System;
using System.Data;

namespace SkyBuilding.MySql
{
    public class MySqlAdapter : IDbConnectionAdapter
    {
        public const string Name = "MySql";
        public string ProviderName => Name;

        public Type ConnectionType => typeof(MySqlConnection);

        public virtual ISQLCorrectSettings Settings => Singleton<MySqlCorrectSettings>.Instance;

        public virtual double ConnectionHeartbeat => 5D;

        public virtual IDbConnection Create(string connectionString) => new MySqlConnection(connectionString);
    }
}
