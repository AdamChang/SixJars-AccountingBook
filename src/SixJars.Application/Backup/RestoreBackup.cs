using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Backup;

/// <summary>
/// 把 <see cref="ExportBackup"/> 的備份還原成一本帳（CLI <c>restore-backup</c>），回傳帳本 Id；所有 Id 都沿用備份裡的。
/// 只給 CLI 用（管理工具，操作者不是任何帳本的成員），所以是 internal 的 <see cref="ICliOnlyRequest"/>。
/// </summary>
/// <remarks>
/// 一律經過 Domain 重新建立與驗證，不直接把 JSON 寫進資料表：損毀或被手動改壞的備份在這一步失敗，不會寫入不一致的資料。
/// Domain 重建的結果與備份不同（例如被正規化）也視為損毀，不默默改資料。
/// </remarks>
/// <param name="FileName">來源檔名；只記在 Restore 稽核記錄，不含路徑。</param>
internal sealed record RestoreBackup(BackupDocument Backup, string FileName) : IRequest<Guid>, ICliOnlyRequest;

internal sealed class RestoreBackupValidator : AbstractValidator<RestoreBackup>
{
    public RestoreBackupValidator()
    {
        RuleFor(c => c.Backup.FormatVersion)
            .InclusiveBetween(1, BackupDocument.CurrentFormatVersion)
            .WithMessage($"只支援格式版本 1 到 {BackupDocument.CurrentFormatVersion} 的備份。");
        RuleFor(c => c.FileName).NotEmpty();
    }
}

internal sealed class RestoreBackupHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<RestoreBackup, Guid>
{
    public async Task<Guid> Handle(RestoreBackup request, CancellationToken cancellationToken)
    {
        var backup = request.Backup;
        var bookId = new BookId(backup.Book.Id);

        // 1. 不覆蓋既有的帳本：還原只用在空的資料庫（搬家或災難復原）。
        if (await db.Books.AnyAsync(b => b.Id == bookId, cancellationToken))
        {
            throw new DomainException($"資料庫中已有帳本 {backup.Book.Id}，不覆蓋；請還原到沒有這本帳的資料庫。");
        }

        // 2–5. 先在記憶體中經 Domain 重建全部資料，任何驗證失敗都發生在寫入之前。
        var book = RestoreBook(backup.Book, backup.FormatVersion);
        var transactions = backup.Transactions.Select(t => RestoreTransaction(book, t)).ToList();
        var plannedExpenses = RestorePlannedExpenses(book, backup.PlannedExpenses, transactions.ToDictionary(t => t.Transaction.Id));

        // 交易的刪除放在付款連結（MarkPaid）之後：已刪除的預定支出可以連結到之後才被刪除的付款交易（spec §9 O2），
        // 照實際發生的順序重建，日後 MarkPaid 若加上「交易不可已刪除」的檢查也不受影響。
        foreach (var (transaction, deletedAt) in transactions)
        {
            if (deletedAt is { } at)
            {
                transaction.Delete(at);
            }
        }

        // 鎖帳日在交易全部建立之後才設定。TransactionBuilder 不檢查鎖帳日（那是寫入 command 的責任），
        // 所以目前順序不影響結果；但若日後有人在 Domain 加上檢查，先設定鎖帳日會讓鎖住期間的交易還原失敗。
        book.SetLockDate(backup.Book.LockDate);

        var members = backup.Members
            .Select(m => BookMember.Restore(m.Id, bookId, m.Email, m.GoogleSubject, m.Role, m.AddedAt))
            .ToList();

        // 6–7. 依相依順序分段寫入（同 ImportLegacyBook），整段包在同一個 DB transaction：任何一段失敗都整個回滾。
        await using var dbTransaction = await db.BeginTransactionAsync(cancellationToken);
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);

        db.Transactions.AddRange(transactions.Select(t => t.Transaction));
        db.PlannedExpenses.AddRange(plannedExpenses);
        await db.SaveChangesAsync(cancellationToken);

        db.BookMembers.AddRange(members);
        // 稽核記錄原樣寫回（Id、時間、操作者與快照都不變），再加一筆這次還原的記錄（操作者為 CLI）。
        db.AuditEntries.AddRange(backup.AuditEntries.Select(e => new AuditEntry
        {
            Id = e.Id,
            BookId = backup.Book.Id,
            At = e.At,
            ActorSubject = e.ActorSubject,
            Action = e.Action,
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            Before = e.Before?.GetRawText(),
            After = e.After?.GetRawText(),
        }));
        audit.Record(backup.Book.Id, AuditAction.Restore, AuditEntityTypes.Book, backup.Book.Id, null, new RestoreSnapshot(
            request.FileName, backup.ExportedAt, transactions.Count, plannedExpenses.Count, members.Count, backup.AuditEntries.Count));
        await db.SaveChangesAsync(cancellationToken);

        await dbTransaction.CommitAsync(cancellationToken);
        return backup.Book.Id;
    }

    /// <summary>先建主分類、再建子分類（子分類要找得到主分類）；鎖帳日由呼叫端在交易建立之後設定。</summary>
    /// <remarks>SortOrder 不另外寫入：照 DTO 順序 Add 會重建出相同的連續編號（P4 J plan D5），一致性檢查會驗證這一點。</remarks>
    private static Book RestoreBook(BookDto dto, int formatVersion)
    {
        var book = new Book(dto.Name, dto.OpeningDate, new BookId(dto.Id));
        foreach (var account in dto.Accounts)
        {
            book.AddAccount(account.Name, account.Type, account.OpeningBalance, account.CountsAsAvailableCash, new AccountId(account.Id));
        }

        foreach (var fund in dto.PlanningFunds)
        {
            book.AddPlanningFund(fund.Name, fund.OpeningBalance, new PlanningFundId(fund.Id));
        }

        foreach (var category in dto.Categories.Where(c => c.ParentId is null))
        {
            _ = category.Kind == CategoryKind.Income
                ? book.AddIncomeCategory(category.Name, new CategoryId(category.Id))
                : book.AddExpenseCategory(category.Name,
                    category.Nature ?? throw new DomainException($"支出主分類「{category.Name}」缺少支出性質。"), new CategoryId(category.Id));
        }

        foreach (var category in dto.Categories.Where(c => c.ParentId is not null))
        {
            book.AddSubCategory(new CategoryId(category.ParentId!.Value), category.Name, new CategoryId(category.Id));
        }

        // 封存的帳戶在備份當下已通過餘額檢查，交易也會原樣還原，所以這裡以 0 傳入。
        foreach (var account in dto.Accounts.Where(a => a.ArchivedAt is not null))
        {
            book.ArchiveAccount(new AccountId(account.Id), balance: 0m, account.ArchivedAt!.Value);
        }

        foreach (var fund in dto.PlanningFunds.Where(f => f.ArchivedAt is not null))
        {
            book.ArchivePlanningFund(new PlanningFundId(fund.Id), balance: 0m, fund.ArchivedAt!.Value);
        }

        foreach (var category in dto.Categories.Where(c => c.ArchivedAt is not null))
        {
            book.ArchiveCategory(new CategoryId(category.Id), category.ArchivedAt!.Value);
        }

        // 例如名稱被去掉空白、非現金帳戶的「計入可用現金」被改成 false、子分類的種類與主分類不同。
        // v1 沒有 SortOrder（全部是 0），重建出來的是依清單順序的編號，比對時忽略。
        var expected = formatVersion == 1 ? WithoutSortOrder(dto) : dto;
        var restored = formatVersion == 1 ? WithoutSortOrder(BookDto.From(book)) : BookDto.From(book);
        if (!restored.Accounts.SequenceEqual(expected.Accounts)
            || !restored.PlanningFunds.SequenceEqual(expected.PlanningFunds)
            || !restored.Categories.ToHashSet().SetEquals(expected.Categories)
            || restored.Name != dto.Name)
        {
            throw new DomainException($"帳本 {dto.Id} 的設定經 Domain 重建後與備份不一致，備份可能已損毀。");
        }

        return book;
    }

    private static BookDto WithoutSortOrder(BookDto book) => book with
    {
        Accounts = [.. book.Accounts.Select(a => a with { SortOrder = 0 })],
        PlanningFunds = [.. book.PlanningFunds.Select(f => f with { SortOrder = 0 })],
        Categories = [.. book.Categories.Select(c => c with { SortOrder = 0 })],
    };

    /// <summary>以輸入欄位重建（分錄由 factory 重新展開，不採用備份裡的分錄）；刪除留到付款連結之後。</summary>
    private static (Transaction Transaction, DateTimeOffset? DeletedAt) RestoreTransaction(Book book, BackupTransaction backup)
    {
        var input = backup.Transaction.ToInput();
        var transaction = TransactionBuilder.Build(book, input, new TransactionId(backup.Transaction.Id));
        if (TransactionDto.From(transaction, backup.Transaction.Version).ToInput() != input)
        {
            throw new DomainException($"交易 {backup.Transaction.Id} 經 Domain 重建後與備份不一致，備份可能已損毀。");
        }

        return (transaction, backup.DeletedAt);
    }

    private static List<PlannedExpense> RestorePlannedExpenses(
        Book book,
        IReadOnlyList<BackupPlannedExpense> backups,
        Dictionary<TransactionId, (Transaction Transaction, DateTimeOffset? DeletedAt)> transactions)
    {
        var paidTransactions = new HashSet<TransactionId>();
        var restored = new List<PlannedExpense>();
        foreach (var (dto, deletedAt) in backups)
        {
            var planned = PlannedExpense.Create(
                book, BudgetMonth.FromKey(dto.BudgetMonth), new CategoryId(dto.CategoryId),
                dto.AccountId is { } accountId ? new AccountId(accountId) : null, dto.EstimatedAmount, dto.Note, new PlannedExpenseId(dto.Id));

            if (dto.PaidTransactionId is { } paidId)
            {
                if (!transactions.TryGetValue(new TransactionId(paidId), out var payment))
                {
                    throw new DomainException($"預定支出 {dto.Id} 的付款交易 {paidId} 不在備份中，備份可能已損毀。");
                }

                // API 走不到的狀態：刪除付款交易時，未刪除的預定支出一定會解除連結（O2）；一筆交易也只會是一個預定支出的付款。
                if (payment.DeletedAt is not null && deletedAt is null)
                {
                    throw new DomainException($"預定支出 {dto.Id} 未刪除，卻連結到已刪除的付款交易 {paidId}，備份可能已損毀。");
                }

                if (!paidTransactions.Add(payment.Transaction.Id))
                {
                    throw new DomainException($"交易 {paidId} 是多筆預定支出的付款，備份可能已損毀。");
                }

                planned.MarkPaid(payment.Transaction);
            }

            if (PlannedExpenseDto.From(planned, dto.Version) != dto)
            {
                throw new DomainException($"預定支出 {dto.Id} 經 Domain 重建後與備份不一致，備份可能已損毀。");
            }

            // 已付款的預定支出也可能已刪除：先 MarkPaid 再 Delete（已刪除的不能再付款）。
            if (deletedAt is { } at)
            {
                planned.Delete(at);
            }

            restored.Add(planned);
        }

        return restored;
    }

    /// <summary>Restore 稽核記錄的 After：來源檔名、備份的匯出時間與各類筆數。</summary>
    private sealed record RestoreSnapshot(
        string FileName, DateTimeOffset ExportedAt, int Transactions, int PlannedExpenses, int Members, int AuditEntries);
}
