# 0029. Only `Result<TError>` converts to `bool`

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, §9.

On a valued result the conversion
said whether the operation succeeded where a reader expects the value: `if (await
IsEmailTakenAsync(email))` over `Result<bool, DbFault>` took the branch for `Success(false)`. The
same reason keeps a bare error from converting into one. Match it, or compare `State`.
