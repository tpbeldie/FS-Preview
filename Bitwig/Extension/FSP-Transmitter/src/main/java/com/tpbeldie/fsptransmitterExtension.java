package com.tpbeldie;

import com.bitwig.extension.callback.ShortMidiMessageReceivedCallback;
import com.bitwig.extension.controller.api.ControllerHost;
import com.bitwig.extension.controller.ControllerExtension;
import com.bitwig.extension.controller.api.Parameter;
import com.bitwig.extension.controller.api.Transport;
import com.bitwig.extension.api.util.midi.ShortMidiMessage;
import com.bitwig.extension.callback.DoubleValueChangedCallback;

import java.io.*;
import java.net.URI;
import java.net.URISyntaxException;

import org.apache.http.HttpEntity;
import org.apache.http.client.methods.CloseableHttpResponse;
import org.apache.http.client.methods.HttpPost;
import org.apache.http.entity.ContentType;
import org.apache.http.entity.StringEntity;
import org.apache.http.impl.client.CloseableHttpClient;
import org.apache.http.impl.client.HttpClients;

public class fsptransmitterExtension extends ControllerExtension {

   private Transport mTransport;
   private String userHomePath;
   private String userFolderPath;
   private boolean platformMac;
   private boolean platformWin;
   private boolean platformLinux;
   private Transport transport;
   private ControllerHost host;
   private boolean isPlaying = false;

   protected fsptransmitterExtension(final fsptransmitterExtensionDefinition definition, final ControllerHost host) {
      super(definition, host);
   }

   @Override
   public void init() {
      host = getHost();
      host.showPopupNotification("FSP-Transmitter Initialized");
      mTransport = host.createTransport();

      this.platformMac = host.platformIsMac();
      this.platformLinux = host.platformIsLinux();
      this.platformWin = host.platformIsWindows();

      this.userHomePath = System.getProperty("user.home");
      this.userFolderPath = this.userHomePath + "/.tpbeldie";

      final File folderPath = new File(this.userFolderPath);
      folderPath.mkdir();

      // Transport Start
      mTransport.playStartPositionInSeconds().addValueObserver(s -> {
          handleTransportStartPositionChange(s);
      });

      // Transport Change
      mTransport.getPosition().addValueObserver(t -> {
          handleTransportPositionChange(t);
      });

      // Tempo
      mTransport.tempo().value().addValueObserver(t -> {
         host.println("BPM updated: " + String.format("%.2f", FormatBPM(t)));
         handleTempoChange(FormatBPM(t));
      });

      // Play state
      mTransport.isPlaying().addValueObserver(p -> {
         if (p != isPlaying) {
            handlePlayStateChanged(p);
            isPlaying = p;
         }
      });
   }

   public double FormatBPM(double bpm) {
      // BPM = (maximum tempo - minimum tempo) * double bpm + minimum tempo
      return (666.00 - 20.00) * bpm + 20.00;
   }

   protected void handleTransportStartPositionChange(final double value) {
      sendRequest("Transport-Start", value);
      writeToFile("transport", String.valueOf(value));
   }

   protected void handleTransportPositionChange(final double value) {
      sendRequest("Transport-Position", value);
   }

   protected void handleTempoChange(final double value) {
      sendRequest("Tempo", value);
      writeToFile("tempo", String.valueOf(value));
   }

   protected void handlePlayStateChanged(final Boolean value) {
      sendRequest("Play-State", value ? 1 : 0);
   }

   protected void sendRequest(String parmeter, final double value) {
      try {
         URI uri = new URI("http://localhost:8080");
         String payload = parmeter + ":" + String.valueOf(value);
         HttpEntity entity = new StringEntity(payload, ContentType.APPLICATION_JSON);
         HttpPost post = new HttpPost(uri);
         post.setEntity(entity);
         try (CloseableHttpClient httpClient = HttpClients.createDefault();
              CloseableHttpResponse response = httpClient.execute(post)) {
         }
      } catch (IOException e) {
      } catch (URISyntaxException e) {
      }
   }

   protected void writeToFile(String fileName, String content)
   {
       String filePath = this.userFolderPath + "/" + fileName;
       final File file = new File(filePath);
         try {
            final FileWriter fileWriter = new FileWriter(file);
            fileWriter.write(content);
            fileWriter.close();
      } catch (IOException e) { }
   }

   @Override
   public void exit() {
      getHost().showPopupNotification("FSP-Transmitter Exited");
   }

   @Override
   public void flush() {
   }
}
