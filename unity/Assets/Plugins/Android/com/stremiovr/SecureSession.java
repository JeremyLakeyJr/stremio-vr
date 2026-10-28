package com.stremiovr;
import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
public final class SecureSession {
 private static final String ALIAS="stremiovr-session";
 private static SecretKey key() throws Exception {
  KeyStore store=KeyStore.getInstance("AndroidKeyStore"); store.load(null);
  if(!store.containsAlias(ALIAS)) { KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore"); generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build()); generator.generateKey(); }
  return (SecretKey)store.getKey(ALIAS,null);
 }
 public static void save(Context context,String value) throws Exception {
  Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE,key());
  String encrypted=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(cipher.doFinal(value.getBytes("UTF-8")),Base64.NO_WRAP);
  if(!context.getSharedPreferences(ALIAS,Context.MODE_PRIVATE).edit().putString("session",encrypted).commit()) throw new Exception("Session storage failed");
 }
 public static String load(Context context) throws Exception {
  String value=context.getSharedPreferences(ALIAS,Context.MODE_PRIVATE).getString("session",""); if(value.isEmpty()) return "";
  String[] parts=value.split(":"); Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(parts[0],Base64.NO_WRAP))); return new String(cipher.doFinal(Base64.decode(parts[1],Base64.NO_WRAP)),"UTF-8");
 }
 public static void clear(Context context) { context.getSharedPreferences(ALIAS,Context.MODE_PRIVATE).edit().clear().commit(); }
}
