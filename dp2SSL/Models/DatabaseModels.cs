﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace dp2SSL
{
    public class RequestItem
    {
        // public Operator Operator { get; set; }  // 提起请求的读者
        // Operator 对象 JSON 化以后的字符串
        public string OperatorString { get; set; }

        // public Entity Entity { get; set; }
        // Entity 对象 JSON 化以后的字符串
        public string EntityString { get; set; }

        public string Action { get; set; }  // borrow/return/transfer
        public string TransferDirection { get; set; } // in/out 典藏移交的方向
        public string Location { get; set; }    // 所有者馆藏地。transfer 动作会用到
        public string CurrentShelfNo { get; set; }  // 当前架号。transfer 动作会用到
        public string BatchNo { get; set; } // 批次号。transfer 动作会用到。建议可以用当前用户名加上日期构成
    }

#if NO
    public class RequestItem
    {
        // public Operator Operator { get; set; }  // 提起请求的读者
        public string PatronName { get;set }
        public string PatronBarcode { get; set; }

        // public Entity Entity { get; set; }
        public string UID { get; set; }
        public string ReaderName { get; set; }
        public string Antenna { get; set; }
        public string PII { get; set; }
        public string ItemRecPath { get; set; }
        public string Title { get; set; }
        public string ItemLocation { get; set; }
        public string ItemCurrentLocation { get; set; }
        public string ShelfNo { get; set; }
        public string State { get; set; }

        public string Action { get; set; }  // borrow/return/transfer
        public string TransferDirection { get; set; } // in/out 典藏移交的方向
        public string Location { get; set; }    // 所有者馆藏地。transfer 动作会用到
        public string CurrentShelfNo { get; set; }  // 当前架号。transfer 动作会用到
        public string BatchNo { get; set; } // 批次号。transfer 动作会用到。建议可以用当前用户名加上日期构成
    }
#endif

    public class MyContext : DbContext
    {
        public DbSet<RequestItem> Requests { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseSqlite(@"Data Source=actions.db;");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RequestItem>().ToTable("requests");
        }
    }
}