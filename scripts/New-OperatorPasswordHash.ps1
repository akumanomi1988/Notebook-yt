$ErrorActionPreference = 'Stop'
$password = Read-Host 'Operator password' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ($plain.Length -lt 12) { throw 'Use at least 12 characters.' }
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt)
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new($plain, $salt, 600000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $hash = '600000:' + [Convert]::ToBase64String($salt) + ':' + [Convert]::ToBase64String($derive.GetBytes(32))
    Write-Output $hash
    $derive.Dispose()
    $rng.Dispose()
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $plain = $null
    $password.Dispose()
}
