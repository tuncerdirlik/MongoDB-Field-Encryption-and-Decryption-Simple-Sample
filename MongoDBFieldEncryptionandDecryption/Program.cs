using AutoMapper;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Encryption;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MongoDBFieldEncryptionandDecryption
{
    public class Employee
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string Name { get; set; }
        public string EmpID { get; set; }
        public BsonValue Ssn { get; set; }
        public string State { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
    }

    public class EmployeeDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string EmpID { get; set; }
        public string Ssn { get; set; }
        public string State { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
    }


    public class AutoMapperProfile : Profile
    {
        private readonly ClientEncryption clientEncryption;

        public AutoMapperProfile(ClientEncryption clientEncryption)
        {
            this.clientEncryption = clientEncryption;

            CreateMap<Employee, EmployeeDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()))
                .ForMember(dest => dest.Ssn, opt => opt.MapFrom(src => clientEncryption.Decrypt(src.Ssn.AsBsonBinaryData, CancellationToken.None).ToString()));
        }
    }

    class Program
    {
        readonly static string databseName = "ConsoleAppTest";
        readonly static string collectionName = "TestCollection";
        readonly static string connectionString = "mongodb://localhost:27017";

        static string LocalMasterKey = "z6y8NecZcrC8zkCb8phTLp/hdnJpABsQ643xVgr5C0/pfO/p7jjYkqTXgzvsyYCZS/xvexE+U7BHAxZB4GmvD3aoSz3LJ3D+TQyBbj5R79cQL9s4WUxG8tvPhR/gOfRO";

        static void Main(string[] args)
        {
            var localMasterKeyBytes = Convert.FromBase64String(LocalMasterKey);
            var kmsProviders = new Dictionary<string, IReadOnlyDictionary<string, object>>();
            var localOptions = new Dictionary<string, object>
           {
                 { "key", localMasterKeyBytes }
           };
            kmsProviders.Add("local", localOptions);

            var keyVaultClient = new MongoClient(connectionString);
            var keyVaultNamespace = new CollectionNamespace(databseName, "__keymaterial");


            //  ClientEncryption instance
            var clientEncryptionSettings = new ClientEncryptionOptions(keyVaultClient, keyVaultNamespace, kmsProviders);

            using (var clientEncryption = new ClientEncryption(clientEncryptionSettings))
            {
                var config = new MapperConfiguration(cfg => {
                    cfg.AddProfile(new AutoMapperProfile(clientEncryption));
                });

                IMapper mapper = config.CreateMapper();

                var clientSettings = MongoClientSettings.FromUrl(new MongoUrl(connectionString));
                

                var client = new MongoClient(connectionString);
                var database = client.GetDatabase(databseName);
                var collection = database.GetCollection<Employee>(collectionName);

                string originalSsn = "1234";

                var dataKeyId = clientEncryption.CreateDataKey(
                    "local",
                    new DataKeyOptions(),
                    CancellationToken.None);

                Console.WriteLine($"Original string {originalSsn}.");

                // Explicitly encrypt a field
                var encryptOptions = new EncryptOptions(
                  EncryptionAlgorithm.AEAD_AES_256_CBC_HMAC_SHA_512_Deterministic.ToString(),
                    keyId: dataKeyId);


                var SocialSecurityNumber = clientEncryption.Encrypt(
                    originalSsn,
                    encryptOptions,
                    CancellationToken.None);

                Console.WriteLine($"Encrypted value {SocialSecurityNumber}.");

                var filterDefinition = Builders<Employee>.Filter.Eq(p => p.EmpID, "100");
                var employees = collection.Find(filterDefinition).ToList();
                if (employees.Count == 0)
                {
                    collection.InsertOne(new Employee
                    {
                        Name = "John",
                        EmpID = "100",
                        Ssn = "1234",
                        State = "NY",
                        City = "NY",
                        Country = "USA"
                    });
                }

                var updateDefinition = Builders<Employee>.Update.Set(p => p.Ssn, SocialSecurityNumber);

                collection.UpdateOne(filterDefinition, updateDefinition);

                var encryptedEmployees = collection.Find(new BsonDocument()).ToList();

                List<EmployeeDto> employeeDtoList = mapper.Map<List<EmployeeDto>>(encryptedEmployees);

                //foreach (var employee in encryptedEmployees)
                //{
                //    var decryptedSsn = clientEncryption.Decrypt(employee.Ssn.AsBsonBinaryData, CancellationToken.None);

                //    EmployeeDto employeDto = new EmployeeDto();
                //    employeDto.Id = employee.Id.ToString();
                //    employeDto.Name = employee.Name;
                //    employeDto.EmpID = employee.EmpID;
                //    employeDto.Ssn = decryptedSsn.ToString();
                //    employeDto.State = employee.State;
                //    employeDto.City = employee.City;
                //    employeDto.Country = employee.Country;
                //}

                
            }


            Console.WriteLine("Hello World!");
        }
    }
}
