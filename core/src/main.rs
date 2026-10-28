use std::io::{self, Read};
use serde_json::{json, Value};
use stremio_core::types::{addon::Manifest, resource::{Stream, StreamUrls}};
use url::Url;

fn http_url(value: &str) -> Result<Url, String> {
    let url = Url::parse(value).map_err(|e| e.to_string())?;
    if !matches!(url.scheme(), "http" | "https") || !url.username().is_empty() || url.password().is_some() {
        return Err("Use an HTTP(S) URL without embedded credentials".into());
    }
    Ok(url)
}
fn handle(request: Value) -> Result<Value, String> {
    match request["operation"].as_str() {
        Some("manifest") => {
            let manifest: Manifest = serde_json::from_value(request["manifest"].clone()).map_err(|e| e.to_string())?;
            serde_json::to_value(manifest).map_err(|e| e.to_string())
        }
        Some("resolve") => {
            let stream: Stream = serde_json::from_value(request["stream"].clone()).map_err(|e| e.to_string())?;
            let service = request["streamingService"].as_str().filter(|s| !s.is_empty()).map(http_url).transpose()?;
            if request["stream"]["behaviorHints"]["proxyHeaders"].is_object() && service.is_none() {
                return Err("A streaming service is required for custom source headers".into());
            }
            let converted = stream.convert(service.as_ref()).map_err(|e| e.to_string())?;
            let urls = StreamUrls::new(converted, service.as_ref());
            let url = urls.streaming_url.ok_or("Source requires an external player")?;
            let url = http_url(url.as_str())?;
            Ok(json!({"url": url.as_str()}))
        }
        _ => Err("Unknown core operation".into()),
    }
}
fn main() {
    let mut input = String::new();
    let result = io::stdin().take(65537).read_to_string(&mut input)
        .map_err(|e| e.to_string())
        .and_then(|_| if input.len() > 65536 { Err("Request too large".into()) } else { serde_json::from_str(&input).map_err(|e| e.to_string()) })
        .and_then(handle);
    match result { Ok(data) => println!("{}", json!({"ok": true, "data": data})), Err(error) => println!("{}", json!({"ok": false, "error": error})) }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn direct_signed_url() { let url = "https://example.com/a.mp4?token=a%2Fb"; assert_eq!(handle(json!({"operation":"resolve","stream":{"url":url}})).unwrap()["url"], url); }
    #[test] fn torrent_uses_stremio_service() {
        let result = handle(json!({"operation":"resolve","stream":{"infoHash":"0123456789012345678901234567890123456789","fileIdx":2},"streamingService":"http://192.168.1.2:11470"})).unwrap();
        assert_eq!(Url::parse(result["url"].as_str().unwrap()).unwrap().path(), "/0123456789012345678901234567890123456789/2");
    }
    #[test] fn missing_torrent_index_uses_largest() {
        let result = handle(json!({"operation":"resolve","stream":{"infoHash":"0123456789012345678901234567890123456789"},"streamingService":"http://192.168.1.2:11470"})).unwrap();
        assert!(Url::parse(result["url"].as_str().unwrap()).unwrap().path().ends_with("/-1"));
    }
    #[test] fn invalid_sources_fail() {
        for stream in [json!({"url":"file:///etc/passwd"}), json!({"url":"https://user:secret@example.com/a"}), json!({"externalUrl":"https://example.com/player"}), json!({"infoHash":"invalid"})] {
            assert!(handle(json!({"operation":"resolve","stream":stream})).is_err());
        }
    }
    #[test] fn manifests_use_core_schema() {
        let manifest = json!({"id":"test","name":"Test","version":"1.0.0","types":["movie"],"resources":["catalog","stream"],"catalogs":[]});
        assert_eq!(handle(json!({"operation":"manifest","manifest":manifest})).unwrap()["id"], "test");
        assert!(handle(json!({"operation":"manifest","manifest":{"id":"broken"}})).is_err());
    }
    #[test] fn headers_require_service_and_are_routed_by_core() {
        let stream = json!({"url":"https://example.com/a.mp4","behaviorHints":{"proxyHeaders":{"request":{"User-Agent":"Test"}}}});
        assert!(handle(json!({"operation":"resolve","stream":stream})).is_err());
        let result = handle(json!({"operation":"resolve","stream":stream,"streamingService":"http://localhost:11470"})).unwrap();
        assert!(result["url"].as_str().unwrap().contains("/proxy/"));
    }
}
