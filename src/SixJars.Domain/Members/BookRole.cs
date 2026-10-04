namespace SixJars.Domain.Members;

/// <summary>帳本成員的角色（CONTEXT.md 帳本成員）。P2 只建立、也只授權 <see cref="Owner"/>（spec §3.4）。</summary>
public enum BookRole
{
    /// <summary>擁有者：可讀寫帳本的全部內容。</summary>
    Owner,

    /// <summary>記帳者：已定義，P2 不支援。</summary>
    Bookkeeper,

    /// <summary>唯讀：已定義，P2 不支援。</summary>
    ReadOnly,
}
