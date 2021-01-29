﻿using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Docs.Documents;
using Volo.Docs.Projects;

namespace Volo.Docs.EntityFrameworkCore
{
    [IgnoreMultiTenancy]
    [ConnectionStringName(DocsDbProperties.ConnectionStringName)]
    public interface IDocsDbContext : IEfCoreDbContext
    {
        DbSet<Project> Projects { get; set; }

        DbSet<Document> Documents { get; set; }

        DbSet<DocumentContributor> DocumentContributors { get; set; }
    }
}
