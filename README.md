# TCP Message & File Transfer (Windows Forms / .NET Framework 4.8)

Bu proje iki bilgisayar (veya aynı bilgisayarda iki uygulama) arasında basit TCP ile **mesaj** ve **dosya** gönderir. Aynı uygulama hem sunucu hem istemci olarak kullanılabilir.

## Çalıştırma

1. Visual Studio 2022'de `TcpMessageTransfer.sln` dosyasını açın.
2. Hedef framework olarak **.NET Framework 4.8** seçili olmalıdır.
3. Projeyi iki kez çalıştırın.
4. Bir pencerede portu (ör. `5000`) seçip **Sunucuyu Başlat**'a basın.
5. Diğer pencerede IP (aynı bilgisayar için `127.0.0.1`) ve aynı portu yazıp **Bağlan**'a basın.
6. Mesaj yazıp **Mesaj Gönder**, dosya seçip **Dosya Gönder** düğmelerini kullanın.

Gelen dosyalar varsayılan olarak uygulama klasöründeki `AlinanDosyalar` dizinine kaydedilir. İsterseniz **Klasör Seç** ile değiştirebilirsiniz.

## Protokol

Her paket şu şekilde çerçevelenir: `1 byte tür` + `4 byte big-endian payload uzunluğu` + `payload`. Mesaj payload'ı UTF-8 metindir. Dosya payload'ı ise dosya adı uzunluğu, dosya adı ve dosya içeriğinden oluşur. Bu çerçeveleme sayesinde tek TCP akışında birden fazla mesaj/dosya güvenli biçimde ayrıştırılır.

> Bu örnek eğitim amaçlıdır. Kimlik doğrulama ve şifreleme içermez; güvenilmeyen ağlarda kullanmayın.
